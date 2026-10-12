using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using SharpGLTF.Schema2;
namespace Aion2Importer;
public sealed record AnimationBone(string Name,int Parent,float[] Rest);
public sealed record AnimationSection(string Name,int StartFrame,int EndFrame,string? Next){public override string ToString()=>Name;}
public sealed record AnimationVariant(string Name,string File){public override string ToString()=>Name;}
public sealed record AnimationClipData(string Name,string Skeleton,int Fps,double Duration,AnimationBone[] Bones,float[][][] Frames,AnimationSection[]? Sections=null,string Kind="Sequence",string? Slot=null) {
    public static AnimationClipData Load(string path) {
        if(new FileInfo(path).Length>64*1024*1024)throw new InvalidDataException("Animação acima do limite da prévia.");
        var clip=JsonSerializer.Deserialize<AnimationClipData>(File.ReadAllText(path))??throw new InvalidDataException("Animação vazia.");
        if(clip.Bones.Length is <1 or >512||clip.Frames.Length is <1 or >1800||clip.Fps is <1 or >60||!double.IsFinite(clip.Duration)||clip.Duration<=0||clip.Duration>60)throw new InvalidDataException("Animação inválida.");
        for(int i=0;i<clip.Bones.Length;i++)if(clip.Bones[i].Parent>=i||clip.Bones[i].Parent< -1)throw new InvalidDataException("Hierarquia inválida.");
        if(clip.Bones.Any(b=>b.Rest.Length!=10||b.Rest.Any(v=>!float.IsFinite(v))))throw new InvalidDataException("Pose base inválida.");
        foreach(var frame in clip.Frames)if(frame.Length!=clip.Bones.Length||frame.Any(t=>t.Length!=10||t.Any(v=>!float.IsFinite(v))))throw new InvalidDataException("Quadro inválido.");
        if(clip.Sections is {Length:>0} sections){
            if(sections.Length>128||sections.Select(s=>s.Name).Distinct().Count()!=sections.Length||sections.Any(s=>s.StartFrame<0||s.EndFrame<=s.StartFrame||s.EndFrame>clip.Frames.Length||s.Next!=null&&!sections.Any(n=>n.Name==s.Next)))throw new InvalidDataException("Seções inválidas na montagem.");
        }
        return clip;
    }
    public Matrix4x4[] World(float[][] transforms) {
        var world=new Matrix4x4[Bones.Length];
        for(int i=0;i<world.Length;i++) {
            var t=transforms[i];
            var local=Matrix4x4.CreateScale(t[7],t[8],t[9])*Matrix4x4.CreateFromQuaternion(System.Numerics.Quaternion.Normalize(new(t[3],t[4],t[5],t[6])))*Matrix4x4.CreateTranslation(t[0],t[1],t[2]);
            world[i]=Bones[i].Parent<0?local:local*world[Bones[i].Parent];
        }
        return world;
    }
}
public sealed class AnimatedMesh {
    sealed record Part(MeshGeometry3D Geometry,Vector3[] Positions,Vector3[] Normals,Vector4[] Joints,Vector4[] Weights,Matrix4x4[] Inverse,int[] Bones);
    readonly List<Part> parts=[];
    readonly List<(GeometryModel3D Model,SharpGLTF.Schema2.Material? Material)> materials=[];
    public int TexturedParts {get;private set;}
    public int ApplyTextures(string root){
        if(TexturedParts>0)return TexturedParts;
        var metadata=Directory.EnumerateFiles(root,"*.json",SearchOption.AllDirectories).GroupBy(Path.GetFileNameWithoutExtension,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key!,g=>g.ToArray(),StringComparer.OrdinalIgnoreCase);
        foreach(var part in materials){var bitmap=PreviewLoader.ReadTexture(part.Material,root,metadata);if(bitmap==null)continue;var brush=new ImageBrush(bitmap){TileMode=TileMode.Tile,ViewportUnits=BrushMappingMode.Absolute,Viewport=new System.Windows.Rect(0,0,1,1)};brush.Freeze();var material=new DiffuseMaterial(brush);material.Freeze();part.Model.Material=material;part.Model.BackMaterial=material;TexturedParts++;}return TexturedParts;
    }
    public Model3DGroup Scene {get;}=new();
    public int Vertices=>parts.Sum(p=>p.Positions.Length);
    public AnimatedMesh(string path,AnimationClipData clip) {
        if(new FileInfo(path).Length>64*1024*1024)throw new InvalidDataException("Modelo acima de 64 MB para animação.");
        var model=ModelRoot.Load(path);
        var boneLookup=clip.Bones.Select((b,i)=>(b.Name,i)).ToDictionary(b=>b.Name,b=>b.i,StringComparer.OrdinalIgnoreCase);
        var rest=clip.World(clip.Bones.Select(b=>b.Rest).ToArray());
        foreach(var node in model.LogicalNodes.Where(n=>n.Mesh!=null&&n.Skin!=null)) {
            var skin=node.Skin!;var joints=skin.Joints.ToArray();var inverse=skin.InverseBindMatrices.ToArray();
            var mapped=joints.Select(j=>boneLookup.GetValueOrDefault(j.Name??"",-1)).ToArray();
            foreach(var primitive in node.Mesh!.Primitives) {
                var positions=primitive.GetVertexAccessor("POSITION")?.AsVector3Array().ToArray();
                var jointIds=primitive.GetVertexAccessor("JOINTS_0")?.AsVector4Array().ToArray();
                var weights=primitive.GetVertexAccessor("WEIGHTS_0")?.AsVector4Array().ToArray();
                if(positions==null||jointIds==null||weights==null)continue;
                if(Vertices+positions.Length>50000)throw new InvalidDataException("Prévia animada limitada a 50 mil vértices para conter o uso de CPU.");
                var used=jointIds.SelectMany((v,i)=>new[]{(v.X,weights[i].X),(v.Y,weights[i].Y),(v.Z,weights[i].Z),(v.W,weights[i].W)})
                    .Where(p=>p.Item2>0).Select(p=>(int)p.Item1).Distinct().ToArray();
                foreach(var j in used) {
                    if(j<0||j>=mapped.Length||mapped[j]<0)throw new InvalidDataException("O modelo e a animação não possuem todos os ossos em comum.");
                    var identity=inverse[j]*rest[mapped[j]];
                    if(Vector3.Distance(identity.Translation,Vector3.Zero)>.02f||
                        Vector3.Distance(new(identity.M11,identity.M12,identity.M13),Vector3.UnitX)>.05f||
                        Vector3.Distance(new(identity.M21,identity.M22,identity.M23),Vector3.UnitY)>.05f)
                        throw new InvalidDataException("Pose base incompatível. Retargeting automático não é aplicado.");
                }
                var geometry=new MeshGeometry3D();
                foreach(var (a,b,c) in primitive.GetTriangleIndices()){geometry.TriangleIndices.Add(a);geometry.TriangleIndices.Add(b);geometry.TriangleIndices.Add(c);}
                var material=new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(154,181,199)));material.Freeze();
                if(primitive.GetVertexAccessor("TEXCOORD_0") is {} uv)foreach(var point in uv.AsVector2Array())geometry.TextureCoordinates.Add(new(point.X,point.Y));
                var rendered=new GeometryModel3D(geometry,material){BackMaterial=material};Scene.Children.Add(rendered);materials.Add((rendered,primitive.Material));
                parts.Add(new(geometry,positions,primitive.GetVertexAccessor("NORMAL")?.AsVector3Array().ToArray()??[],jointIds,weights,inverse,mapped));
            }
        }
        if(parts.Count==0)throw new InvalidDataException("Este GLB não contém uma malha com pesos de esqueleto.");
    }
    public void Update(Matrix4x4[] world) {
        foreach(var part in parts) {
            var skin=part.Inverse.Select((m,i)=>part.Bones[i]<0?Matrix4x4.Identity:m*world[part.Bones[i]]).ToArray();
            var positions=new Point3DCollection(part.Positions.Length);var normals=new Vector3DCollection(part.Normals.Length);
            for(int i=0;i<part.Positions.Length;i++) {
                var js=part.Joints[i];var ws=part.Weights[i];Vector3 p=default,n=default;
                for(int c=0;c<4;c++) {
                    float weight=c switch{0=>ws.X,1=>ws.Y,2=>ws.Z,_=>ws.W};if(weight<=0)continue;
                    int joint=(int)(c switch{0=>js.X,1=>js.Y,2=>js.Z,_=>js.W});var m=skin[joint];
                    p+=Vector3.Transform(part.Positions[i],m)*weight;
                    if(part.Normals.Length>i)n+=Vector3.TransformNormal(part.Normals[i],m)*weight;
                }
                positions.Add(new(p.X,p.Y,p.Z));
                if(n.LengthSquared()>1e-12f)n=Vector3.Normalize(n);
                if(part.Normals.Length>i)normals.Add(new(n.X,n.Y,n.Z));
            }
            part.Geometry.Positions=positions;part.Geometry.Normals=normals;
        }
    }
}
