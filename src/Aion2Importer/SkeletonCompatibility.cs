using System.Numerics;
using System.IO;
using SharpGLTF.Schema2;
namespace Aion2Importer;

public sealed record SkinBone(string Name,float[] Inverse);
public sealed record SkeletonProfile(SkinBone[] Bones) {
    public static SkeletonProfile Read(string path) {
        var model=ModelRoot.Load(path);var result=new List<SkinBone>();
        foreach(var node in model.LogicalNodes.Where(n=>n.Mesh!=null&&n.Skin!=null)) {
            var joints=node.Skin!.Joints.ToArray();var inverse=node.Skin.InverseBindMatrices.ToArray();
            foreach(var primitive in node.Mesh!.Primitives) {
                var ids=primitive.GetVertexAccessor("JOINTS_0")?.AsVector4Array();var weights=primitive.GetVertexAccessor("WEIGHTS_0")?.AsVector4Array();
                if(ids==null||weights==null)continue;
                var used=ids.SelectMany((v,i)=>new[]{(v.X,weights[i].X),(v.Y,weights[i].Y),(v.Z,weights[i].Z),(v.W,weights[i].W)}).Where(p=>p.Item2>0).Select(p=>(int)p.Item1).Distinct();
                foreach(var j in used) {
                    if(j<0||j>=joints.Length||j>=inverse.Length)throw new InvalidDataException("Índice de osso inválido.");
                    var m=inverse[j];result.Add(new(joints[j].Name??"",[m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44]));
                }
            }
        }
        return new(result.DistinctBy(b=>b.Name+string.Join(",",b.Inverse)).ToArray());
    }
    public bool Matches(AnimationClipData clip) {
        if(Bones.Length==0)return false;
        var lookup=clip.Bones.Select((b,i)=>(b.Name,i)).ToDictionary(b=>b.Name,b=>b.i,StringComparer.OrdinalIgnoreCase);
        var rest=clip.World(clip.Bones.Select(b=>b.Rest).ToArray());
        foreach(var bone in Bones) {
            if(!lookup.TryGetValue(bone.Name,out var index)||bone.Inverse.Length!=16)return false;
            var a=bone.Inverse;if(a.Any(v=>!float.IsFinite(v)))return false;
            var m=new Matrix4x4(a[0],a[1],a[2],a[3],a[4],a[5],a[6],a[7],a[8],a[9],a[10],a[11],a[12],a[13],a[14],a[15])*rest[index];
            if(Vector3.Distance(m.Translation,Vector3.Zero)>.02f||Vector3.Distance(new(m.M11,m.M12,m.M13),Vector3.UnitX)>.05f||Vector3.Distance(new(m.M21,m.M22,m.M23),Vector3.UnitY)>.05f||Vector3.Distance(new(m.M31,m.M32,m.M33),Vector3.UnitZ)>.05f)return false;
        }
        return true;
    }
}
