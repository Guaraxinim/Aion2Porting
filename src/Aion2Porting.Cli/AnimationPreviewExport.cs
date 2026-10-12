using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse_Conversion.Animations;
using CUE4Parse_Conversion.Writers.ActorX.Structs.Animations;
using System.Text.Json;

public static class AnimationPreviewExport {
    sealed record Segment(double Start,double In,double Out,double Rate,int Loops,CAnimSequence Sequence) {
        public double Length=>(Out-In)*Loops/Math.Abs(Rate);
    }
    sealed record Section(string Name,int StartFrame,int EndFrame,string? Next);
    public static float SafeSourceFrame(double sourceSeconds,double sourceFps,int frames) {
        if(frames<1||!double.IsFinite(sourceSeconds)||!double.IsFinite(sourceFps)||sourceFps<=0)
            throw new InvalidDataException("Esqueleto ou duração fora dos limites da prévia.");
        return Math.Clamp((float)(sourceSeconds*sourceFps),0,frames-1);
    }
    public static object Convert(UAnimationAsset animation,string root,string? outputDirectory=null) {
        if(!animation.Skeleton.TryLoad<USkeleton>(out var skeleton)||skeleton==null)throw new InvalidDataException("Não foi possível ler o esqueleto da animação.");
        var output=outputDirectory??Path.Combine(root,"cache","previews","animation-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        var variants=new List<object>();string? first=null;int totalFrames=0;
        var slots=animation switch {
            UAnimMontage montage=>montage.SlotAnimTracks.Select(s=>(Name:s.SlotName.Text,Segments:s.AnimTrack.AnimSegments)).ToArray(),
            UAnimComposite composite=>new[]{(Name:"Composite",Segments:composite.AnimationTrack.AnimSegments)},
            _=>Array.Empty<(string Name,FAnimSegment[] Segments)>()
        };
        var sections=animation is UAnimMontage m?m.CompositeSections:Array.Empty<FCompositeSection>();
        var slotSegments=new List<(string Name,List<Segment> Segments)>();
        if(animation is UAnimCompositeBase) {
            foreach(var slot in slots.Where(s=>s.Segments.Length>0)) {
                var segments=new List<Segment>();
                if(slot.Segments.Length>64)throw new InvalidDataException("Montagem acima do limite de 64 trechos por trilha.");
                foreach(var raw in slot.Segments.OrderBy(s=>s.StartPos)) {
                    if(!raw.AnimReference.TryLoad<UAnimSequence>(out var source)||source==null)throw new InvalidDataException("Não foi possível ler um trecho da montagem.");
                    if(!source.Skeleton.TryLoad<USkeleton>(out var reference)||reference==null||reference.Name!=skeleton.Name)throw new InvalidDataException("Esqueletos incompatíveis na montagem.");
                    var sequence=skeleton.ConvertAnims(source).Sequences.Single();
                    var segment=new Segment(raw.StartPos,raw.AnimStartTime,raw.AnimEndTime,raw.GetValidPlayRate(),raw.LoopingCount,sequence);
                    Validate(segment);
                    if(segments.Count>0&&segment.Start<segments[^1].Start+segments[^1].Length-0.0001)
                        throw new InvalidDataException("Trechos sobrepostos na mesma trilha requerem mistura de poses e ainda não são reproduzidos.");
                    segments.Add(segment);
                }
                slotSegments.Add((slot.Name,segments));
            }
        } else {
            var sequence=animation.ConvertAnims().Sequences.Single();
            var rate=sequence.OriginalSequence.RateScale;
            var segment=new Segment(0,0,sequence.OriginalSequence.SequenceLength,Math.Abs(rate)<0.000001?1:rate,1,sequence);
            Validate(segment);slotSegments.Add(("Sequence",[segment]));
        }
        if(slotSegments.Count==0)throw new InvalidDataException("Nenhum trecho reproduzível na montagem.");
        double duration=slotSegments.Max(s=>s.Segments.Max(a=>a.Start+a.Length));
        if(!double.IsFinite(duration)||duration<=0||duration>60)throw new InvalidDataException("Prévia limitada a sequências de até 60 segundos.");
        var bones=skeleton.ReferenceSkeleton.FinalRefBoneInfo;var rest=skeleton.ReferenceSkeleton.FinalRefBonePose;
        if(bones.Length is <1 or >512||rest.Length!=bones.Length)throw new InvalidDataException("Esqueleto ou duração fora dos limites da prévia.");
        const int fps=30;int frameCount=Math.Max(1,(int)Math.Ceiling(duration*fps-0.00001));
        var authoredSections=sections.OrderBy(s=>s.GetTime()).ToArray();
        var playbackSections=authoredSections.Select(s=>{
            var start=Math.Clamp((int)Math.Round(s.GetTime()*fps),0,frameCount-1);
            var nextTime=authoredSections.Where(a=>a.GetTime()>s.GetTime()+0.00001).Select(a=>(double)a.GetTime()).DefaultIfEmpty(duration).Min();
            var end=Math.Clamp((int)Math.Round(nextTime*fps),start+1,frameCount);
            return new Section(s.SectionName.Text,start,end,s.NextSectionName.IsNone?null:s.NextSectionName.Text);
        }).ToArray();
        foreach(var slot in slotSegments) {
            var frames=new float[frameCount][][];
            for(int frame=0;frame<frameCount;frame++) {
                double time=frame/(double)fps;
                var segment=slot.Segments.LastOrDefault(s=>time>=s.Start-0.000001&&time<s.Start+s.Length);
                double sourceSeconds=0;
                if(segment!=null) {
                    double elapsed=Math.Max(0,(time-segment.Start)*Math.Abs(segment.Rate));
                    double span=segment.Out-segment.In;
                    double position=elapsed>=span*segment.Loops-0.000001?span:elapsed%span;
                    sourceSeconds=segment.Rate<0?segment.Out-position:segment.In+position;
                }
                frames[frame]=new float[bones.Length][];
                for(int bone=0;bone<bones.Length;bone++) {
                    var pose=rest[bone];var rotation=pose.Rotation;var translation=pose.Translation;var scale=pose.Scale3D;
                    if(segment!=null&&bone<segment.Sequence.Tracks.Count) {
                        var sequence=segment.Sequence;
                        // Shared by ordinary sequences, trimmed segments, reverse playback and loops.
                        var sourceFrame=SafeSourceFrame(sourceSeconds,sequence.NumFrames/(double)sequence.OriginalSequence.SequenceLength,sequence.NumFrames);
                        sequence.Tracks[bone].GetBoneTransform(sourceFrame,sequence.NumFrames,ref rotation,ref translation,ref scale);
                    }
                    frames[frame][bone]=Transform(rotation,translation,scale);
                    if(frames[frame][bone].Any(v=>!float.IsFinite(v)))throw new InvalidDataException("Transformação inválida na animação.");
                }
            }
            var file=Path.Combine(output,"animation-"+variants.Count+".json");
            var name=slotSegments.Count==1?animation.Name:animation.Name+" • "+slot.Name;
            File.WriteAllText(file,JsonSerializer.Serialize(new {
                Name=name,Skeleton=skeleton.Name,Fps=fps,Duration=duration,
                Bones=bones.Select((b,i)=>new{Name=b.Name.Text,Parent=b.ParentIndex,Rest=Transform(rest[i].Rotation,rest[i].Translation,rest[i].Scale3D)}),
                Frames=frames,Sections=playbackSections,Kind=animation is UAnimCompositeBase?"Montage":"Sequence",Slot=slot.Name,
                Segments=slot.Segments.Select(s=>new{Name=s.Sequence.Name,s.Start,SourceStart=s.In,SourceEnd=s.Out,s.Rate,s.Loops,Duration=s.Length})
            }));
            first??=file;totalFrames=frameCount;variants.Add(new{Name=slot.Name,File=file});
        }
        return new {Success=true,Animation=first,Name=animation.Name,Skeleton=skeleton.Name,FrameCount=totalFrames,Bones=bones.Length,Variants=variants};
    }
    static void Validate(Segment segment) {
        var sequence=segment.Sequence;
        if(sequence.IsAdditive)throw new InvalidDataException("Animação aditiva requer uma pose base e ainda não é reproduzida.");
        if(sequence.NumFrames<1||!float.IsFinite(sequence.OriginalSequence.SequenceLength)||sequence.OriginalSequence.SequenceLength<=0||
           !double.IsFinite(segment.Start)||!double.IsFinite(segment.In)||!double.IsFinite(segment.Out)||!double.IsFinite(segment.Rate)||
           segment.Start<0||segment.In<0||segment.Out<=segment.In||segment.Out>sequence.OriginalSequence.SequenceLength+0.0001||Math.Abs(segment.Rate)<0.000001||segment.Loops is <1 or >100)
            throw new InvalidDataException("Trecho inválido na montagem.");
    }
    static float[] Transform(CUE4Parse.UE4.Objects.Core.Math.FQuat q,CUE4Parse.UE4.Objects.Core.Math.FVector p,CUE4Parse.UE4.Objects.Core.Math.FVector s)
        =>[p.X*.01f,p.Z*.01f,p.Y*.01f,q.X,q.Z,q.Y,-q.W,s.X,s.Z,s.Y];
}
