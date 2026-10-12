using System.Security.Cryptography;
using System.Text.Json;
using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using CUE4Parse_Conversion;
using CUE4Parse_Conversion.Options;
using Serilog;
using CUE4Parse_Conversion.Animations;
using CUE4Parse_Conversion.Sounds;

var root = Environment.GetEnvironmentVariable("AION2_ROOT") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
Directory.SetCurrentDirectory(root);
var exportRoot = Environment.GetEnvironmentVariable("AION2_EXPORT_ROOT") ?? Path.Combine(root, "exports");
var game = Environment.GetEnvironmentVariable("AION2_GAME_DIR") ?? @"C:\Program Files (x86)\Steam\steamapps\common\AION2\Aion2\Content\Paks";
var chunk = args.Length > 3 ? args[3] : "pakchunk30100-Windows_0_P.utoc";
var mapping = Path.Combine(root, "mappings/AION2-5.3.2.0-1.0.52.0.usmap");
var mode = args.FirstOrDefault() ?? "inventory";
if(mode=="animation-sampling-test"){
    var result=JsonSerializer.Serialize(AnimationSamplingTests.Run());
    Directory.CreateDirectory(Path.Combine(root,"reports","montage-test"));
    File.WriteAllText(Path.Combine(root,"reports","montage-test","sampling-test.json"),result);Console.WriteLine(result);return 0;
}
var limit = args.Length > 1 ? int.Parse(args[1]) : 10;
var filter = args.Length > 2 && args[2] != "*" ? args[2] : "";
if (limit < 1 || limit > 10000) throw new ArgumentOutOfRangeException(nameof(limit), "Use 1..10000");
if (mode is not ("inventory" or "probe" or "export" or "chunks" or "batch" or "catalog" or "audio" or "audio-discovery" or "inspect" or "animation-probe" or "animation" or "scan-server")) throw new ArgumentException("Use chunks, inventory, probe or export [limit] [path substring]");
var run = Path.Combine(root, "reports", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + mode);
Directory.CreateDirectory(run);
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
void Save(string name, object value) => File.WriteAllText(Path.Combine(run, name), JsonSerializer.Serialize(value, jsonOptions));
Log.Logger = new LoggerConfiguration().MinimumLevel.Warning().WriteTo.Console().CreateLogger();
if (args.Length > 4)
{
    var manifestPath = Path.GetFullPath(args[4]);
    var reportRoot = Path.Combine(root, "reports") + Path.DirectorySeparatorChar;
    if (!manifestPath.StartsWith(reportRoot, StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException("Manifest must be inside project reports");
    File.WriteAllText(manifestPath, JsonSerializer.Serialize(new {
        ReportDirectory = run,
        Inventory = Path.Combine(run, "inventory.json"),
        Chunks = Path.Combine(run, "chunks.json"),
        OutputDirectory = Path.Combine(exportRoot, Path.GetFileName(run))
    }, jsonOptions));
}

var records = new List<object>();
try
{
    await OodleHelper.InitializeAsync(Path.Combine(root, "tools", OodleHelper.OodleFileName));
    using var provider = new DefaultFileProvider(game, SearchOption.TopDirectoryOnly,
        new VersionContainer(EGame.GAME_Aion2), StringComparer.OrdinalIgnoreCase);
    provider.MappingsContainer = new FileUsmapTypeMappingsProvider(mapping);
    provider.Initialize();
    await provider.MountAsync();
    await provider.SubmitKeyAsync(new FGuid(), new FAesKey("0x06038EF544B6007614F8574F1B7C2A3F0D565F74CDCC1B366B4EA1A17B97CBFF"));
    provider.PostMount();
    if(mode=="scan-server") {
        Directory.Delete(run,true);
        System.Runtime.InteropServices.NativeLibrary.SetDllImportResolver(typeof(CUE4Parse.ACL.ACLNative).Assembly,
            (name,assembly,search)=>name=="CUE4Parse-Natives"?System.Runtime.InteropServices.NativeLibrary.Load(Path.Combine(root,"tools","CUE4Parse-Natives.dll")):IntPtr.Zero);
        Console.WriteLine("SCAN_READY");Console.Out.Flush();
        while(Console.ReadLine() is string request) {
            var current=Path.Combine(root,"reports","catalog-scan","work");
            if(Directory.Exists(current))Directory.Delete(current,true);
            Directory.CreateDirectory(current);
            try {
                var path=JsonSerializer.Deserialize<string>(request)??throw new InvalidDataException("Empty path");
                await AssetInspector.Inspect(provider,path,root,current,true);
                string? animationFile=null;
                var package=provider.LoadPackage(provider.Files[path]);
                foreach(var lazy in package.ExportsLazy.Take(128)) {
                    if(lazy.Value is not CUE4Parse.UE4.Assets.Exports.Animation.UAnimationAsset animation)continue;
                    using var converted=JsonDocument.Parse(JsonSerializer.Serialize(AnimationPreviewExport.Convert(animation,root,Path.Combine(current,"animation"))));
                    animationFile=converted.RootElement.GetProperty("Animation").GetString();
                    break;
                }
                Console.WriteLine("SCAN_RESULT "+JsonSerializer.Serialize(new{Inspection=Path.Combine(current,"inspection.json"),Animation=animationFile}));
            }catch(Exception ex){Console.WriteLine("SCAN_RESULT "+JsonSerializer.Serialize(new{Error=ex.Message}));}
            Console.Out.Flush();
        }
        return 0;
    }
    if(mode is "animation-probe" or "animation") {
        System.Runtime.InteropServices.NativeLibrary.SetDllImportResolver(typeof(CUE4Parse.ACL.ACLNative).Assembly,
            (name,assembly,search)=>name=="CUE4Parse-Natives"?System.Runtime.InteropServices.NativeLibrary.Load(Path.Combine(root,"tools","CUE4Parse-Natives.dll")):IntPtr.Zero);
        var package=provider.LoadPackage(provider.Files[filter]);
        foreach(var lazy in package.ExportsLazy) {
            if(lazy.Value is not CUE4Parse.UE4.Assets.Exports.Animation.UAnimationAsset animation)continue;
            if(mode=="animation"){Save("animation.json",AnimationPreviewExport.Convert(animation,root));return 0;}
            var converted=animation.ConvertAnims();
            Save("animation-probe.json",new {Name=animation.Name,Skeleton=converted.Skeleton.Name,Sequences=converted.Sequences.Select(s=>new{s.Name,s.NumFrames,s.FramesPerSecond,s.IsAdditive,Tracks=s.Tracks.Count,Keys=s.Tracks.Sum(t=>t.KeyPos.Length+t.KeyQuat.Length)})});
            return 0;
        }
        throw new InvalidDataException("Este asset não é uma sequência de animação.");
    }
    bool IsRawAudio(string path)=>new[]{".wem",".wav",".ogg",".mp3",".opus",".binka"}.Contains(Path.GetExtension(path),StringComparer.OrdinalIgnoreCase);
    if(mode=="audio-discovery") {
        var paths=provider.Files.Keys.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var raw=paths.Where(IsRawAudio).ToArray();
        Save("audio-discovery.json",new {
            TotalRaw=raw.Length,
            Formats=raw.GroupBy(Path.GetExtension).Select(g=>new{Format=g.Key,Count=g.Count(),Samples=g.Take(6)}),
            Events=paths.Count(p=>p.Contains("/WwiseAudio/Events/",StringComparison.OrdinalIgnoreCase)&&p.EndsWith(".uasset")),
            EventGroups=paths.Where(p=>p.Contains("/WwiseAudio/Events/")&&p.EndsWith(".uasset")).GroupBy(p=>p.Split("/Events/")[1].Split('/')[0]).Select(g=>new{Group=g.Key,Count=g.Count(),Samples=g.Take(2)}),
            OtherCandidates=paths.Where(p=>p.EndsWith(".uasset")&&(Path.GetFileName(p).StartsWith("SW_")||p.Contains("/Audio/")||p.Contains("/Sounds/"))).Take(40),
            RawSamples=raw.Take(80)
        });return 0;
    }
    if(mode=="inspect") {
        await AssetInspector.Inspect(provider,filter,root,run);return 0;
    }

    if(mode=="audio") {
        if(!provider.Files.TryGetValue(filter,out var audioFile))throw new FileNotFoundException("Asset de áudio não encontrado.",filter);
        var audioDirectory=Path.Combine(root,"cache","previews","audio-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(audioDirectory);
        var outputFiles=new List<string>();
        if(IsRawAudio(audioFile.Path)) {
            if(audioFile.Size>128*1024*1024)throw new InvalidDataException("Áudio muito grande para prévia.");
            var target=Path.Combine(audioDirectory,Path.GetFileName(audioFile.Path));
            File.WriteAllBytes(target,audioFile.Read());outputFiles.Add(target);
            Save("audio.json",new{Files=outputFiles,Asset=filter});return 0;
        }
        var pendingAudio=new Queue<(string Path,int Depth)>();pendingAudio.Enqueue((filter,0));
        var visitedAudio=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var emittedMedia=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool emptyAct=false;
        while(pendingAudio.Count>0 && visitedAudio.Count<24 && outputFiles.Count<64) {
        var currentAudio=pendingAudio.Dequeue();if(!visitedAudio.Add(currentAudio.Path))continue;
        var package=provider.LoadPackage(provider.Files[currentAudio.Path]);
        foreach(var lazy in package.ExportsLazy.Take(128)) {
            var obj=lazy.Value;
            var objectJson=Newtonsoft.Json.Linq.JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(obj));
            File.WriteAllText(Path.Combine(run,"object-"+Guid.NewGuid().ToString("N")+".json"),objectJson.ToString());
            if(currentAudio.Depth==0&&objectJson["Type"]?.ToString()=="AionAct")
                emptyAct=!objectJson.Descendants().OfType<Newtonsoft.Json.Linq.JProperty>().Any(p=>p.Name=="TaskLinks"&&p.Value is Newtonsoft.Json.Linq.JArray tasks&&tasks.Count>0);
            if(currentAudio.Depth<4)foreach(var reference in objectJson.Descendants().OfType<Newtonsoft.Json.Linq.JProperty>().Where(p=>p.Name is "ObjectPath" or "AssetPathName")) {
                var target=reference.Value.ToString();
                if(!target.StartsWith("/Game/",StringComparison.OrdinalIgnoreCase))continue;
                target="AION2/Content/"+target[6..];
                var dot=target.LastIndexOf('.');if(dot>target.LastIndexOf('/'))target=target[..dot];
                target+=".uasset";
                if(visitedAudio.Contains(target)||!provider.Files.ContainsKey(target)||pendingAudio.Count>=48)continue;
                if(!target.Contains("/WwiseAudio/",StringComparison.OrdinalIgnoreCase)&&!target.Contains("/Sound",StringComparison.OrdinalIgnoreCase)&&!target.Contains("/Audio/",StringComparison.OrdinalIgnoreCase))continue;
                records.Add(new{From=currentAudio.Path,Reference=target});
                pendingAudio.Enqueue((target,currentAudio.Depth+1));
            }

            if(obj is CUE4Parse.UE4.Assets.Exports.Wwise.UAkAudioEvent evt && evt.EventCookedData is {} cooked) {
                var media=cooked.EventLanguageMap.Values.Where(v=>v.HasValue).SelectMany(v=>v!.Value.Media.Concat(v.Value.SwitchContainerLeaves.SelectMany(l=>l.Media)));
                foreach(var item in media.DistinctBy(m=>m.MediaPathName.Text)) {
                    if(outputFiles.Count>=64)break;
                    var suffix="/"+item.MediaPathName.Text.Replace('\\','/');
                    var matches=provider.Files.Keys.Where(p=>p.EndsWith(suffix,StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).Take(64).ToArray();
                    records.Add(new{Media=item.MediaPathName.Text,Matches=matches});
                    foreach(var match in matches) {
                        if(outputFiles.Count>=64)break;
                        if(!emittedMedia.Add(match))continue;
                        var raw=provider.Files[match];
                        if(raw.Size>128*1024*1024)throw new InvalidDataException("Áudio excede o limite de prévia (128 MB).");
                        var target=Path.Combine(audioDirectory,outputFiles.Count.ToString("D3")+"_"+Path.GetFileName(match));
                        File.WriteAllBytes(target,raw.Read());outputFiles.Add(target);
                    }
                }
            }

            obj.Decode(true,out var format,out var bytes);
            if(bytes is not {Length:>0})continue;
            if(!System.Text.RegularExpressions.Regex.IsMatch(format,"^[A-Za-z0-9]+$"))continue;
            var audioPath=Path.Combine(audioDirectory,outputFiles.Count.ToString("D3")+"."+format.ToLowerInvariant());
            File.WriteAllBytes(audioPath,bytes);outputFiles.Add(audioPath);
        }
        }
        Save("audio.json",new {Files=outputFiles,Asset=filter,PackagesRead=visitedAudio.Count,Limited=pendingAudio.Count>0});Save("audio-sources.json",records);
        if(outputFiles.Count==0)throw new InvalidDataException((emptyAct?"Este AionAct foi lido, mas suas seções não têm tarefas de áudio ou referências de mídia reconhecidas. ":"Não foi encontrada mídia de áudio nas referências reconhecidas deste pacote. ")+"Consulte "+run);
        return 0;
    }

    if (mode == "catalog") {
        Save("catalog.json", new {
            GeneratedAtUtc = DateTime.UtcNow,
            Files = provider.Files.Values.Where(f => f.IsUePackage||IsRawAudio(f.Path)).Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Select(p => provider.Files[p]).OrderBy(f => f.Path).Select(f => new {
                f.Path, f.Size, Chunk = (f as CUE4Parse.UE4.VirtualFileSystem.VfsEntry)?.Vfs.Name ?? ""
            })
        });
        Console.WriteLine($"Catalog saved: {run}");
        return 0;
    }
    if (mode == "chunks")
    {
        var catalog = Directory.EnumerateFiles(game, "*.utoc").OrderBy(p => p).Where(p => !Path.GetFileName(p).Equals("global.utoc", StringComparison.OrdinalIgnoreCase))
            .Select(p => {
                var name = Path.GetFileName(p);
                var reader = provider.MountedVfs.FirstOrDefault(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                var folders = reader?.Files.Values.GroupBy(f => {
                    var parts = f.Path.Split('/');
                    var index = Array.FindIndex(parts, s => s.Equals("Content", StringComparison.OrdinalIgnoreCase));
                    if (index < 0 || index + 1 >= parts.Length - 1) return "Outros";
                    var first = parts[index + 1];
                    return first.Equals("Character", StringComparison.OrdinalIgnoreCase) && index + 2 < parts.Length - 1
                        ? first + "/" + parts[index + 2] : first;
                }).Select(g => new { Folder = g.Key, Files = g.Count() }).OrderByDescending(g => g.Files).ToArray();
                return new { Name = name, Mounted = reader != null, FileCount = reader?.Files.Count ?? 0, Folders = folders };
            }).ToArray();
        Save("chunks.json", catalog);
        Console.WriteLine($"Chunks cataloged={catalog.Length}. Report: {run}");
        return 0;
    }
    var files = (chunk == "*" ? provider.Files.Values.Where(x => x.Path.StartsWith(filter, StringComparison.OrdinalIgnoreCase)).Select(x => x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Select(p => provider.Files[p]) : provider.GetArchive(chunk).Files.Values).OrderBy(x => x.Path).ToArray();
    Save("inventory.json", files.Select(x => new { x.Path, x.Size, x.IsUePackage }));
    Save("mount.json", new { Game = "GAME_Aion2", MappingSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(mapping))),
        Mounted = provider.MountedVfs.Select(x => x.Name), Unloaded = provider.UnloadedVfs.Select(x => x.Name),
        GlobalDataLoaded = provider.GlobalData != null, Target = chunk, TargetFileCount = files.Length, TotalFiles = provider.Files.Count });
    Console.WriteLine($"Mounted={provider.MountedVfs.Count}, Unloaded={provider.UnloadedVfs.Count}, TargetFiles={files.Length}");
    if (mode == "inventory") return 0;
    var selected = files.Where(x => x.IsUePackage && x.Path.Contains(filter, StringComparison.OrdinalIgnoreCase))
        .Take(limit);
    if (mode == "batch")
    {
        if (args.Length < 6) throw new ArgumentException("Batch requires a selection JSON");
        var selectionPath = Path.GetFullPath(args[5]);
        if (!selectionPath.StartsWith(Path.Combine(root, "reports") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Selection must be in project reports");
        using var document = JsonDocument.Parse(File.ReadAllText(selectionPath));
        var paths = document.RootElement.EnumerateArray().Select(x => x.GetProperty("Path").GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var byPath = files.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
        if (paths.Any(p => !byPath.ContainsKey(p))) throw new ArgumentException("Selected path missing from mounted inventory");
        selected = paths.OrderBy(p => p).Select(p => byPath[p]).ToArray();
        Save("selection.json", selected.Select(f => new { f.Path }));
    }
    var batchItems = new List<object>();
    int fileNumber = 0;
    int meshes = 0, written = 0, glbs = 0, failures = 0;
    foreach (var file in selected)
    {
        fileNumber++;
        int beforeGlbs = glbs, beforeFailures = failures;
        var assetOutput = Path.Combine(exportRoot, Path.GetFileName(run), mode == "batch" ? $"{fileNumber:D4}_{Path.GetFileNameWithoutExtension(file.Path)}" : "");
        Console.WriteLine($"READ {fileNumber}: {file.Path}");
        try
        {
            var package = provider.LoadPackage(file);
            foreach (var lazy in package.ExportsLazy)
            {
                var obj = lazy.Value;
                bool isMesh = obj is USkeletalMesh or UStaticMesh;
                records.Add(new { file.Path, Object = obj.Name, Type = obj.GetType().Name, Mesh = isMesh });
                Console.WriteLine($"  {obj.GetType().Name}: {obj.Name}");
                if (!isMesh) continue;
                meshes++;
                if (mode is not ("export" or "batch" or "catalog" or "audio" or "audio-discovery" or "inspect" or "animation-probe" or "animation")) continue;
                var output = assetOutput;
                var session = new ExportSession { MaxDegreeOfParallelism = 1 };
                session.Add(obj);
                var results = await session.RunAsync(output, new ExportOptions(meshFormat: EMeshFormat.Gltf2));
                foreach (var result in results)
                {
                    var diskFiles = result.DiskFilePaths?.Select(p => new { Path = p, Exists = File.Exists(p), Bytes = File.Exists(p) ? new FileInfo(p).Length : 0 }).ToArray();
                    records.Add(new { result.ObjectPath, result.Success, Files = diskFiles, Error = result.Error?.ToString() });
                    if (result.Success && diskFiles is { Length: > 0 } && diskFiles.All(f => f.Exists && f.Bytes > 0))
                    {
                        written++;
                        glbs += diskFiles.Count(f => f.Path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase));
                    }
                    else failures++;
                }
            }
        }
        catch (Exception ex) { failures++; records.Add(new { file.Path, Error = ex.ToString() }); Console.WriteLine(ex); }
        if (mode == "batch") {
            if (glbs == beforeGlbs && failures == beforeFailures) failures++;
            batchItems.Add(new { file.Path, OutputDirectory = assetOutput, GlbFiles = glbs - beforeGlbs, Failures = failures - beforeFailures });
            Save("batch-items.json", batchItems);
        }
        Save("results.json", records);
    }
    Save("summary.json", new { MeshesDeserialized = meshes, SuccessfulExportResultsWithFiles = written, GlbFiles = glbs, Failures = failures, BlenderValidated = false });
    Console.WriteLine($"Meshes={meshes}, export results with files={written}. Report: {run}");
    return failures > 0 ? 3 : mode is ("export" or "batch") ? (glbs > 0 ? 0 : 2) : (meshes > 0 ? 0 : 2);
}
catch (Exception ex) { Save("fatal.json", new { Message=ex.Message, Error = ex.ToString() }); Console.Error.WriteLine(ex); return 1; }
