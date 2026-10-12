using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Aion2Importer;
public partial class MainWindow {
    readonly Dictionary<string,HashSet<string>> assetTemporaryFiles=new(StringComparer.OrdinalIgnoreCase);
    bool retryingCache;
    string ReadSettings(){var legacy=System.IO.Path.Combine(root,"reports","desktop-settings.json");var dat=System.IO.Path.Combine(root,"cache","settings.dat");if(File.Exists(legacy))CompactDat.Migrate(legacy,dat,"settings.json");return CompactDat.Read(dat,"settings.json",input=>new StreamReader(input).ReadToEnd());}
    bool IsCacheFailure(Exception ex)=>ex is IOException or UnauthorizedAccessException or JsonException;
    void TrackTemporaryFiles(string request,string result){
        if(!File.Exists(request)||!File.Exists(result))return;
        try{
            using var selection=JsonDocument.Parse(File.ReadAllText(request));
            if(selection.RootElement.ValueKind!=JsonValueKind.Array||selection.RootElement.GetArrayLength()!=1)return;
            var asset=selection.RootElement[0].GetProperty("Path").GetString()!;
            if(!assetTemporaryFiles.TryGetValue(asset,out var files))assetTemporaryFiles[asset]=files=new(StringComparer.OrdinalIgnoreCase);
            using var data=JsonDocument.Parse(File.ReadAllText(result));
            void Visit(JsonElement element){if(element.ValueKind==JsonValueKind.String){var value=element.GetString()!;if(System.IO.Path.IsPathFullyQualified(value)&&IsTransientPath(value))files.Add(System.IO.Path.GetFullPath(value));}else if(element.ValueKind==JsonValueKind.Object)foreach(var property in element.EnumerateObject())Visit(property.Value);else if(element.ValueKind==JsonValueKind.Array)foreach(var item in element.EnumerateArray())Visit(item);}
            Visit(data.RootElement);files.Add(result);
        }catch(JsonException){}catch(KeyNotFoundException){}
    }
    bool IsTransientPath(string path){var full=System.IO.Path.GetFullPath(path);var preview=System.IO.Path.GetFullPath(System.IO.Path.Combine(root,"cache","previews"))+System.IO.Path.DirectorySeparatorChar;var reports=System.IO.Path.GetFullPath(System.IO.Path.Combine(root,"reports"))+System.IO.Path.DirectorySeparatorChar;return full.StartsWith(preview,StringComparison.OrdinalIgnoreCase)||full.StartsWith(reports,StringComparison.OrdinalIgnoreCase)&&Regex.IsMatch(System.IO.Path.GetRelativePath(reports,full),@"^(\d{8}-\d{6}-\d{3}-|desktop-result-)");}
    void ResetAssetCache(Asset asset){
        ClosePreview();
        if(previews.Remove(game+"|"+asset.Path,out var saved)){
            if(!assetTemporaryFiles.TryGetValue(asset.Path,out var files))assetTemporaryFiles[asset.Path]=files=new(StringComparer.OrdinalIgnoreCase);
            files.Add(saved.AssetRoot);foreach(var file in saved.Models)files.Add(file);
        }
        if(assetTemporaryFiles.Remove(asset.Path,out var paths))foreach(var path in paths.Where(IsTransientPath).OrderByDescending(p=>p.Length)){
            if(File.Exists(path))File.Delete(path);
            // Only generated directories owned by the application may be removed.
            else if(Directory.Exists(path)){var resolved=System.IO.Path.GetFullPath(path);if((File.GetAttributes(resolved)&FileAttributes.ReparsePoint)!=0)throw new IOException("Cache contém atalho de diretório.");Directory.Delete(resolved,true);}
        }
        var temporaryDat=System.IO.Path.Combine(root,"cache","temporary.dat");
        if(File.Exists(temporaryDat)&&paths!=null){var prefixes=paths.Where(IsTransientPath).Select(p=>System.IO.Path.GetRelativePath(root,p).Replace(System.IO.Path.DirectorySeparatorChar,'/')).ToArray();CompactDat.Update(temporaryDat,zip=>{foreach(var entry in zip.Entries.Where(e=>prefixes.Any(prefix=>e.FullName.Equals(prefix,StringComparison.OrdinalIgnoreCase)||e.FullName.StartsWith(prefix.TrimEnd('/')+"/",StringComparison.OrdinalIgnoreCase))).ToArray())entry.Delete();});}
        availability?.Invalidate(asset);asset.Prepare();availability?.Apply(new[]{asset});
    }
    async Task<bool> RecoverAssetCache(Asset asset,Exception ex,Func<Task> retry){
        if(stopped||retryingCache||!IsCacheFailure(ex))return false;
        retryingCache=true;
        try{ResetAssetCache(asset);Notify("Cache do asset limpo. Tentando novamente a partir dos arquivos do jogo…",Notice.Warning);SetBusy(false);await retry();return true;}
        catch(Exception recoveryError){Notify("Não foi possível recuperar o cache: "+recoveryError.Message,Notice.Error);return true;}
        finally{retryingCache=false;}
    }
    void CompactIdleData(bool closing=false){
        if(testing||PreviewCacheCleanup.OtherInstance(root))return;
        var reports=System.IO.Path.Combine(root,"reports");var cache=System.IO.Path.Combine(root,"cache");Directory.CreateDirectory(cache);
        var generated=new List<string>();
        foreach(var file in Directory.EnumerateFiles(reports))if(Regex.IsMatch(System.IO.Path.GetFileName(file),@"^(desktop-[0-9a-f]{32}|desktop-result-|(?:preview|inspect|audio|animation)(?:-queue|-model)?-selection-|animation-model-|animation-export-[0-9a-f]{32})"))generated.Add(file);else if(System.IO.Path.GetFileName(file).EndsWith("-error.txt",StringComparison.OrdinalIgnoreCase)||System.IO.Path.GetExtension(file).Equals(".dmp",StringComparison.OrdinalIgnoreCase))generated.Add(file);
        foreach(var directory in Directory.EnumerateDirectories(reports).Where(p=>Regex.IsMatch(System.IO.Path.GetFileName(p),@"^\d{8}-\d{6}-\d{3}-")))generated.AddRange(Directory.EnumerateFiles(directory,"*",SearchOption.AllDirectories));
        CompactDat.PackFiles(root,System.IO.Path.Combine(cache,"diagnostics.dat"),generated);
        foreach(var directory in Directory.EnumerateDirectories(reports).Where(p=>Regex.IsMatch(System.IO.Path.GetFileName(p),@"^\d{8}-\d{6}-\d{3}-")))RemoveEmptyDirectories(directory);
        if(closing||!queue.Any(q=>q.Status!="Concluído")){
            var previewsFolder=System.IO.Path.Combine(cache,"previews");
            if(Directory.Exists(previewsFolder)){CompactDat.PackFiles(root,System.IO.Path.Combine(cache,"temporary.dat"),Directory.EnumerateFiles(previewsFolder,"*",SearchOption.AllDirectories));RemoveEmptyDirectories(previewsFolder);}
        }
        var staging=System.IO.Path.Combine(root,"exports");
        if(Directory.Exists(staging)){
            var directories=Directory.EnumerateDirectories(staging).Where(p=>Regex.IsMatch(System.IO.Path.GetFileName(p),@"^(\d{8}-\d{6}-\d{3}-|animation-[0-9a-f]{32}$)")&&!queue.Any(q=>q.ModelFile!=null&&q.ModelFile.StartsWith(p+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))).ToArray();
            CompactDat.PackFiles(root,System.IO.Path.Combine(cache,"staging.dat"),directories.SelectMany(d=>Directory.EnumerateFiles(d,"*",SearchOption.AllDirectories)));
            foreach(var directory in directories)RemoveEmptyDirectories(directory);
        }
        if(File.Exists(System.IO.Path.Combine(cache,"available-models.dat")))CompactDat.PackFiles(root,System.IO.Path.Combine(cache,"legacy.dat"),new[]{System.IO.Path.Combine(cache,"available-models.json"),System.IO.Path.Combine(cache,"model-thumbnails.zip")});
        var summary=JsonSerializer.Serialize(new{UpdatedUtc=DateTime.UtcNow,DiagnosticsBytes=File.Exists(System.IO.Path.Combine(cache,"diagnostics.dat"))?new FileInfo(System.IO.Path.Combine(cache,"diagnostics.dat")).Length:0,ArchivedFiles=generated.Count,NativeFilesExtractedOnlyWhileInUse=true});
        CompactDat.Put(System.IO.Path.Combine(cache,"storage-info.dat"),"summary.json",summary);
    }
    static void RemoveEmptyDirectories(string directory){foreach(var child in Directory.EnumerateDirectories(directory)){if((File.GetAttributes(child)&FileAttributes.ReparsePoint)!=0)continue;RemoveEmptyDirectories(child);}if(!Directory.EnumerateFileSystemEntries(directory).Any())Directory.Delete(directory);}
    async Task PrepareAssetOpening(){SetBusy(true);ClosePreview();try{Notify("Compactando os arquivos temporários…");await Task.Run(()=>CompactIdleData());}catch(Exception ex){Notify("Não foi possível compactar os temporários: "+ex.Message,Notice.Warning);}}
}
