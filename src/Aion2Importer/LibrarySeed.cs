using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Aion2Importer;

public static class LibrarySeed {
    public sealed record Container(string Hash,long DataLength);
    public sealed record Record(ModelAvailabilityCache.Entry Type,string? Image);
    public sealed record Manifest(int Version,string Mapping,Dictionary<string,Container> Containers,List<Record> Records,Dictionary<string,string> Subcategories);
    public static void Verify(string output) {
        Directory.CreateDirectory(output);
        var fixture=Path.Combine(output,"fixture-"+Guid.NewGuid().ToString("N"));
        var paksA=Path.Combine(fixture,"a");var paksB=Path.Combine(fixture,"b");
        Directory.CreateDirectory(paksA);Directory.CreateDirectory(paksB);
        Directory.CreateDirectory(Path.Combine(fixture,"mappings"));Directory.CreateDirectory(Path.Combine(fixture,"cache"));
        var map=Path.Combine(fixture,"mappings","AION2-5.3.2.0-1.0.52.0.usmap");File.WriteAllText(map,"synthetic mapping");
        foreach(var p in new[]{paksA,paksB}){File.WriteAllText(Path.Combine(p,"fixture.utoc"),"same index");File.WriteAllText(Path.Combine(p,"fixture.ucas"),"same data");}
        Asset NewAsset(){var a=new Asset{Path="AION2/Content/Sound/fixture.uasset",Size=123,Chunk="fixture.utoc"};a.Prepare();return a;}
        var initial=new ModelAvailabilityCache(Path.Combine(fixture,"cache","available-models.json"),paksA,map);
        initial.ConfirmAudio(NewAsset(),TimeSpan.FromSeconds(1));initial.SetSubcategory(new[]{NewAsset()},"Seed category");
        var seed=Path.Combine(fixture,"seed.dat");Build(fixture,paksA,seed);
        File.SetLastWriteTimeUtc(Path.Combine(paksB,"fixture.utoc"),DateTime.UtcNow.AddDays(-10));
        var localFile=Path.Combine(paksB,"available-models.json");
        var local=new ModelAvailabilityCache(localFile,paksB,map);local.ImportSeed(map,seed);
        var asset=NewAsset();local.Apply(new[]{asset});
        if(!asset.AudioConfirmed||asset.Section!="Seed category")throw new Exception("Portable seed failed across paths/timestamps.");
        local.SetSubcategory(new[]{asset},"User category");local.Reject(asset);local.ImportSeed(map,seed);local.Apply(new[]{asset});
        if(asset.AudioConfirmed||asset.Section!="User category")throw new Exception("Seed overwrote user data.");
        var reopened=new ModelAvailabilityCache(localFile,paksB,map);reopened.ImportSeed(map,seed);reopened.Apply(new[]{asset});
        if(asset.AudioConfirmed||asset.Section!="User category")throw new Exception("User changes did not persist.");
        File.WriteAllText(Path.Combine(paksB,"fixture.utoc"),"changed index");
        var changed=new ModelAvailabilityCache(Path.Combine(paksB,"changed.json"),paksB,map);changed.ImportSeed(map,seed);asset=NewAsset();changed.Apply(new[]{asset});
        if(asset.AudioConfirmed)throw new Exception("Changed container accepted old type.");
        File.WriteAllText(map,"changed mapping");var changedMap=new ModelAvailabilityCache(Path.Combine(paksB,"changed-map.json"),paksA,map);changedMap.ImportSeed(map,seed);asset=NewAsset();changedMap.Apply(new[]{asset});
        if(asset.AudioConfirmed)throw new Exception("Changed mapping accepted old type.");
        File.WriteAllText(Path.Combine(output,"release-cache-test.json"),JsonSerializer.Serialize(new{Passed=true,PortablePathsAndTimestamps=true,UserSubcategoryPreserved=true,LocalRejectionPreserved=true,ReopenPersisted=true,ChangedContainerRejected=true,ChangedMappingRejected=true}));
    }
    public static Container? Fingerprint(string paks,string chunk) {
        var index=Path.Combine(paks,Path.GetFileName(chunk));var data=Path.ChangeExtension(index,"ucas");
        if(!File.Exists(index)||!File.Exists(data))return null;
        using var input=File.OpenRead(index);
        return new(Convert.ToHexString(SHA256.HashData(input)),new FileInfo(data).Length);
    }
    public static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    public static string ImageKey(ModelAvailabilityCache.Entry e)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(e.Scope+"|"+e.Path.ToUpperInvariant()+"|"+e.Size+"|"+e.Source)))+".jpg";
    public static void Build(string root,string paks,string output) {
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var map=Hash(Path.Combine(root,"mappings","AION2-5.3.2.0-1.0.52.0.usmap"));
        var scope=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(paks).ToUpperInvariant()+"|"+map+"|viewer-v1")));
        using var source=ZipFile.OpenRead(Path.Combine(root,"cache","available-models.dat"));
        using var metadataStream=source.GetEntry("metadata.json")!.Open();
        var metadata=JsonSerializer.Deserialize<ModelAvailabilityCache.Metadata>(metadataStream)!;
        var containers=new Dictionary<string,Container>(StringComparer.OrdinalIgnoreCase);
        var records=new List<Record>();var subs=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        using var zip=ZipFile.Open(output,ZipArchiveMode.Create);
        foreach(var e in metadata.Types.Where(e=>e.Scope==scope)) {
            var index=Path.Combine(paks,Path.GetFileName(e.Chunk));
            string Stamp(string f){var info=new FileInfo(f);return info.Exists?$"{info.Length}:{info.LastWriteTimeUtc.Ticks}":"missing";}
            if(e.Source!=Stamp(index)+"|"+Stamp(Path.ChangeExtension(index,"ucas")))continue;
            if(!containers.ContainsKey(e.Chunk)){var f=Fingerprint(paks,e.Chunk);if(f==null)continue;containers[e.Chunk]=f;}
            var key=ImageKey(e);var image=source.GetEntry(key);
            if(image!=null){using var a=image.Open();using var b=zip.CreateEntry(key,CompressionLevel.NoCompression).Open();a.CopyTo(b);}
            records.Add(new(e,image!=null?key:null));
        }
        var prefix=Path.GetFullPath(paks).ToUpperInvariant()+"|";
        foreach(var sub in metadata.Subcategories)if(sub.Key.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))subs[sub.Key[prefix.Length..]]=sub.Value;
        using var outputStream=zip.CreateEntry("seed.json",CompressionLevel.Optimal).Open();
        JsonSerializer.Serialize(outputStream,new Manifest(1,map,containers,records,subs));
        Console.WriteLine(JsonSerializer.Serialize(new{Types=records.Count,Images=records.Count(r=>r.Image!=null),Containers=containers.Count,Subcategories=subs.Count}));
    }
}
