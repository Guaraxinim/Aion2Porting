using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse_Conversion.Writers.ActorX.Structs.Animations;

public static class AnimationSamplingTests {
    public static object Run(){
        int checks=0;
        foreach(int frames in new[]{1,2,15,75,240})foreach(double fps in new[]{1d,15d,29.97d,30d,30.405405d,60d,120d}){
            var track=new CAnimTrack{KeyPos=Enumerable.Range(0,frames).Select(i=>new FVector(i,0,0)).ToArray(),KeyQuat=[new FQuat(0,0,0,1)]};
            foreach(double time in new[]{-100d,0d,(frames-1)/fps,frames/fps,1e6,1e308}){
                var frame=AnimationPreviewExport.SafeSourceFrame(time,fps,frames);
                var rotation=new FQuat(0,0,0,1);var position=new FVector();var scale=new FVector(1,1,1);
                track.GetBoneTransform(frame,frames,ref rotation,ref position,ref scale);
                if(!float.IsFinite(position.X)||position.X<0||position.X>frames-1||time<0&&position.X!=0||time>=frames/fps&&position.X!=frames-1)throw new Exception("Source frame boundary regression");
                checks++;
            }
        }
        foreach(var input in new[]{(double.NaN,30d,75),(1d,double.PositiveInfinity,75),(1d,30d,0)}){
            try{AnimationPreviewExport.SafeSourceFrame(input.Item1,input.Item2,input.Item3);throw new Exception("Invalid input accepted");}catch(InvalidDataException){checks++;}
        }
        return new{Passed=true,Checks=checks,FractionalFps=true,SingleFrame=true,NegativeTime=true,PastLastFrame=true,VeryLargeTime=true,InvalidInputRejected=true};
    }
}
