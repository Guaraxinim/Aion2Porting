using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Aion2Importer;
public sealed class ModelAvailabilityCache {
    public const string AvailableHint="Modelo disponível";
    public sealed record Entry(string Scope,string Path,long Size,string Chunk,string Source,int Vertices,int Triangles,DateTime VerifiedUtc,string Kind="Model",int PreviewWidth=0,int PreviewHeight=0,SkeletonProfile? Skeleton=null);
    public sealed record Metadata(int Version,List<Entry> Types,Dictionary<string,string> Subcategories);
    readonly string file,paks,scope;
    readonly Dictionary<string,Entry> entries=new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,string> sources=new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,string> subcategories=new(StringComparer.OrdinalIgnoreCase);
    public string? Warning {get;private set;}
    bool unreadable;
    public bool DeferWrites {get;set;}
    public void Flush(){var deferred=DeferWrites;DeferWrites=false;try{UpdateArchive();}finally{DeferWrites=deferred;}}
    public ModelAvailabilityCache(string legacyFile,string paks,string mapping) {
        file=System.IO.Path.ChangeExtension(legacyFile,"dat");
        this.paks=System.IO.Path.GetFullPath(paks);
        var map=File.Exists(mapping)?Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(mapping))):"missing";
        scope=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(this.paks.ToUpperInvariant()+"|"+map+"|viewer-v1")));
        try {
            if(File.Exists(file)) {
                using var zip=ZipFile.OpenRead(file);
                using var input=(zip.GetEntry("metadata.json")??throw new InvalidDataException("Metadados ausentes.")).Open();
                var metadata=JsonSerializer.Deserialize<Metadata>(input)??throw new InvalidDataException("Cache vazio.");
                if(metadata.Version!=1)throw new InvalidDataException("Versão do cache não suportada.");
                foreach(var item in metadata.Types)entries[item.Scope+"|"+item.Path]=item;
                foreach(var item in metadata.Subcategories)subcategories[item.Key]=item.Value;
            } else {
                if(File.Exists(legacyFile)&&!legacyFile.Equals(file,StringComparison.OrdinalIgnoreCase)) {
                    var saved=JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(legacyFile))??[];
                    foreach(var item in saved)entries[item.Scope+"|"+item.Path]=item;
                }
                // Import the old cache once; preserve originals as backups.
                var oldImages=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(file)!,"model-thumbnails.zip");
                UpdateArchive(zip=>{
                    if(!File.Exists(oldImages))return;
                    using var previous=ZipFile.OpenRead(oldImages);
                    foreach(var image in previous.Entries.Where(e=>e.FullName.EndsWith(".jpg",StringComparison.OrdinalIgnoreCase))) {
                        using var input=image.Open();
                        using var output=zip.CreateEntry(image.FullName,CompressionLevel.NoCompression).Open();
                        input.CopyTo(output);
                    }
                });
            }
        }catch(Exception ex){unreadable=true;Warning="Não foi possível ler o cache: "+ex.Message;}
    }
    string Source(Asset asset) {
        if(sources.TryGetValue(asset.Chunk,out var saved))return saved;
        var name=System.IO.Path.GetFileName(asset.Chunk);
        string Stamp(string path) {
            var f=new FileInfo(path);
            return f.Exists?$"{f.Length}:{f.LastWriteTimeUtc.Ticks}":"missing";
        }
        var path=System.IO.Path.Combine(paks,name);
        var value=Stamp(path)+"|"+Stamp(System.IO.Path.ChangeExtension(path,"ucas"));
        sources[asset.Chunk]=value;return value;
    }
    bool Matches(Asset asset,Entry entry)=>entry.Size==asset.Size&&entry.Chunk.Equals(asset.Chunk,StringComparison.OrdinalIgnoreCase)&&entry.Source==Source(asset);
    public bool IsAvailable(Asset asset)=>entries.TryGetValue(scope+"|"+asset.Path,out var entry)&&Matches(asset,entry)&&entry.Kind=="Model"&&entry.Vertices>0&&entry.Triangles>0;
    string SectionKey(Asset asset)=>paks.ToUpperInvariant()+"|"+asset.Path;
    public void Apply(IEnumerable<Asset> assets) {
        foreach(var a in assets) {
            a.SkeletonProfile=null;
            if(entries.TryGetValue(scope+"|"+a.Path,out var e)&&Matches(a,e)) {
                a.SkeletonProfile=e.Skeleton;
                if(e.Kind is "Animation" or "Montage")a.MarkAnimation(e.Kind=="Montage");else if(e.Kind=="Audio")a.MarkAudio();else if(e.Kind=="Texture")a.MarkTexture();else if(e.Vertices>0&&e.Triangles>0)a.MarkModelAvailable();else if(e.Kind!="SkeletonProfile")a.MarkUnclassified();
            }
            if(subcategories.TryGetValue(SectionKey(a),out var section))a.SetSection(section);
        }
    }
    public void SetSubcategory(IEnumerable<Asset> assets,string section) {
        section=section.Trim();
        if(section.Length==0||section.Length>80||section.Any(char.IsControl)||section.Equals("Todas as subcategorias",StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Digite uma subcategoria de 1 a 80 caracteres.");
        var selected=assets.ToArray();
        var before=new Dictionary<string,string>(subcategories,StringComparer.OrdinalIgnoreCase);
        try {
            foreach(var asset in selected)subcategories[SectionKey(asset)]=section;
            UpdateArchive();
        }catch{subcategories.Clear();foreach(var pair in before)subcategories[pair.Key]=pair.Value;throw;}
        foreach(var asset in selected)asset.SetSection(section);
    }
    public void ConfirmAudio(Asset asset,TimeSpan duration) {
        if(duration<=TimeSpan.Zero)throw new InvalidDataException("Duração de áudio não confirmada.");
        asset.MarkAudio();
        SaveEntry(asset,new Entry(scope,asset.Path,asset.Size,asset.Chunk,Source(asset),0,0,DateTime.UtcNow,"Audio"));
    }
    public void Invalidate(Asset asset) {
        if(unreadable){
            if(File.Exists(file))File.Move(file,System.IO.Path.ChangeExtension(file,"corrupt-"+DateTime.UtcNow.Ticks+".dat"));
            unreadable=false;Warning=null;
        }
        entries.Remove(scope+"|"+asset.Path);asset.SetThumbnail(null);asset.SkeletonProfile=null;
        UpdateArchive(zip=>zip.GetEntry(ThumbnailKey(asset))?.Delete());
    }
    public void Reject(Asset asset) {
        asset.MarkUnclassified();
        SaveEntry(asset,new Entry(scope,asset.Path,asset.Size,asset.Chunk,Source(asset),0,0,DateTime.UtcNow));
    }
    public void ConfirmTexture(Asset asset,string path) {
        using var stream=File.OpenRead(path);
        var image=new System.Windows.Media.Imaging.BitmapImage();
        image.BeginInit();image.CacheOption=System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        image.DecodePixelWidth=192;image.StreamSource=stream;image.EndInit();image.Freeze();
        if(image.PixelWidth<=0||image.PixelHeight<=0)throw new InvalidDataException("Textura não confirmada.");
        SaveEntry(asset,new Entry(scope,asset.Path,asset.Size,asset.Chunk,Source(asset),0,0,DateTime.UtcNow,"Texture",image.PixelWidth,image.PixelHeight));
        asset.MarkTexture();
    }
    string ThumbnailKey(Asset asset)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope+"|"+asset.Path.ToUpperInvariant()+"|"+asset.Size+"|"+Source(asset))))+".jpg";
    public void LoadThumbnail(Asset asset) {
        asset.SetThumbnail(null);
        if(!entries.TryGetValue(scope+"|"+asset.Path,out var cached)||!Matches(asset,cached)||cached.Kind!="Model"||!File.Exists(file))return;
        try {
            using var zip=ZipFile.OpenRead(file);
            var entry=zip.GetEntry(ThumbnailKey(asset));if(entry==null)return;
            using var compressed=entry.Open();using var input=new MemoryStream();compressed.CopyTo(input);input.Position=0;
            var image=new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();image.CacheOption=System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.StreamSource=input;image.EndInit();image.Freeze();asset.SetThumbnail(image);
        }catch(Exception ex){Warning="Não foi possível ler a miniatura: "+ex.Message;}
    }
    public void SaveThumbnail(Asset asset,byte[] jpeg) {
        UpdateArchive(zip=>{
            var key=ThumbnailKey(asset);zip.GetEntry(key)?.Delete();
            using var output=zip.CreateEntry(key,CompressionLevel.NoCompression).Open();output.Write(jpeg);
        });
        LoadThumbnail(asset);
    }
    public void ConfirmAnimation(Asset asset,AnimationClipData clip) {
        var montage=clip.Kind=="Montage"&&!(clip.Sections is {Length:1} sections&&string.Equals(sections[0].Name,"Default",StringComparison.OrdinalIgnoreCase));
        SaveEntry(asset,new Entry(scope,asset.Path,asset.Size,asset.Chunk,Source(asset),0,0,DateTime.UtcNow,montage?"Montage":"Animation"));asset.MarkAnimation(montage);
    }
    public void SaveSkeleton(Asset asset,string model) {
        var profile=SkeletonProfile.Read(model);asset.SkeletonProfile=profile;
        if(entries.TryGetValue(scope+"|"+asset.Path,out var entry)&&Matches(asset,entry))SaveEntry(asset,entry with {Skeleton=profile});
        else SaveEntry(asset,new Entry(scope,asset.Path,asset.Size,asset.Chunk,Source(asset),0,0,DateTime.UtcNow,"SkeletonProfile",Skeleton:profile));
    }
    public void Confirm(Asset asset,PreviewScene scene) {
        if(scene.Vertices<=0||scene.Triangles<=0||scene.Bounds.IsEmpty)throw new InvalidDataException("Geometria não confirmada.");
        asset.MarkModelAvailable();
        SaveEntry(asset,new Entry(scope,asset.Path,asset.Size,asset.Chunk,Source(asset),scene.Vertices,scene.Triangles,DateTime.UtcNow));
    }
    void SaveEntry(Asset asset,Entry entry) {
        var key=scope+"|"+asset.Path;entries.TryGetValue(key,out var previous);entries[key]=entry;
        try{UpdateArchive();}
        catch{if(previous==null)entries.Remove(key);else entries[key]=previous;throw;}
    }
    void UpdateArchive(Action<ZipArchive>? update=null) {
        if(DeferWrites&&update==null)return;
        if(unreadable)throw new IOException(Warning+" O arquivo existente foi preservado.");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        var temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp.dat";
        try {
            if(File.Exists(file))File.Copy(file,temporary);
            using(var zip=ZipFile.Open(temporary,ZipArchiveMode.Update)) {
                update?.Invoke(zip);
                zip.GetEntry("metadata.json")?.Delete();
                using var output=zip.CreateEntry("metadata.json",CompressionLevel.Optimal).Open();
                JsonSerializer.Serialize(output,new Metadata(1,entries.Values.ToList(),subcategories));
            }
            File.Move(temporary,file,true);
        }finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
