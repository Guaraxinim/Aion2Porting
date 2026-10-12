using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse_Conversion;
using CUE4Parse_Conversion.Options;
using Newtonsoft.Json.Linq;
using System.Text.Json;

public static class AssetInspector {
    public static async Task Inspect(DefaultFileProvider provider,string path,string root,string report,bool rootOnly=false) {
        if(!provider.Files.TryGetValue(path,out var file))throw new FileNotFoundException("Asset não encontrado.",path);
        var output=Path.Combine(root,"cache","previews","inspect-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        var objects=new List<object>();var references=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnings=new List<string>();var queue=new Queue<(string Path,int Depth)>();queue.Enqueue((path,0));
        var visited=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var models=new List<string>();var images=new List<string>();var imageSources=new Dictionary<string,string>();
        bool hasRootMesh=false;
        string? Resolve(string value) {
            var quote=value.IndexOf((char)39);
            if(quote>=0)value=value[(quote+1)..].TrimEnd((char)39);
            if(value.StartsWith("/Game/"))value="AION2/Content/"+value[6..];
            if(!value.StartsWith("AION2/",StringComparison.OrdinalIgnoreCase))return null;
            var colon=value.IndexOf(':');if(colon>=0)value=value[..colon];
            if(!value.EndsWith(".uasset",StringComparison.OrdinalIgnoreCase)) {
                var dot=value.LastIndexOf('.');if(dot>value.LastIndexOf('/'))value=value[..dot];
                value+=".uasset";
            }
            return provider.Files.ContainsKey(value)?value:null;
        }
        while(queue.Count>0&&visited.Count<20) {
            var item=queue.Dequeue();if(!visited.Add(item.Path))continue;
            try {
                var package=provider.LoadPackage(provider.Files[item.Path]);
                foreach(var lazy in package.ExportsLazy.Take(128)) {
                    try {
                        var obj=lazy.Value;
                        var mesh=obj is USkeletalMesh or UStaticMesh;
                        var texture=obj is UTexture;
                        JObject data;
                        if(mesh||texture)data=new JObject{["Name"]=obj.Name,["Type"]=obj.GetType().Name.TrimStart('U')};
                        else {
                            data=JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(obj));
                            if(!rootOnly)File.WriteAllText(Path.Combine(report,"details-"+objects.Count+"-"+Guid.NewGuid().ToString("N")+".json"),data.ToString());
                        }
                        var type=data["Type"]?.ToString()??obj.GetType().Name;
                        var props=data["Properties"] as JObject;
                        var properties=(props?.Properties()??Enumerable.Empty<JProperty>()).Take(50).Select(p=>{
                            var value=p.Value.ToString(Newtonsoft.Json.Formatting.None);
                            return new{Name=p.Name,Value=value.Length>500?value[..500]+"…":value};
                        }).ToArray();
                        if(item.Depth==0)objects.Add(new{Name=obj.Name,Type=type,Properties=properties});
                        foreach(var property in data.Descendants().OfType<JProperty>().Where(p=>p.Name is "ObjectPath" or "AssetPathName")) {
                            var target=Resolve(property.Value.ToString());
                            if(target==null||target==path)continue;
                            references.Add(target);
                            if(!rootOnly&&item.Depth<2&&queue.Count<40)queue.Enqueue((target,item.Depth+1));
                        }
                        if(!mesh&&!texture)continue;
                        if(mesh&&item.Depth==0)hasRootMesh=true;
                        var session=new ExportSession{MaxDegreeOfParallelism=1};session.Add(obj);
                        var results=await session.RunAsync(output,new ExportOptions(meshFormat:EMeshFormat.Gltf2));
                        foreach(var result in results) {
                            if(!result.Success){warnings.Add(obj.Name+": "+result.Error?.Message);continue;}
                            foreach(var disk in result.DiskFilePaths??[]) {
                                if(disk.EndsWith(".png",StringComparison.OrdinalIgnoreCase)){images.Add(disk);imageSources[disk]=item.Path;}
                                if(disk.EndsWith(".glb",StringComparison.OrdinalIgnoreCase))models.Add(disk);
                            }
                        }
                    }catch(Exception ex){warnings.Add(item.Path+": "+ex.Message);}
                }
            }catch(Exception ex){warnings.Add(item.Path+": "+ex.Message);}
        }
        if(objects.Count==0)throw new InvalidDataException("Não foi possível ler este asset. "+string.Join(" ",warnings.Take(2)));
        File.WriteAllText(Path.Combine(report,"inspection.json"),JsonSerializer.Serialize(new {
            Asset=path,AssetRoot=output,Objects=objects,References=references.Order().ToArray(),
            Images=images.Distinct().Take(24),Models=models.Distinct().Take(12),HasRootMesh=hasRootMesh,
            Warnings=warnings.Take(12),Limited=queue.Count>0,
            Description="Inspeção dos dados e recursos do asset. Partículas, materiais e lógica não são simulados como no jogo."
        },new JsonSerializerOptions{WriteIndented=true}));
    }
}
