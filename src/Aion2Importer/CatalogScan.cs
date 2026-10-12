using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace Aion2Importer;

public partial class MainWindow {
    bool scanning,scanCancelled;
    Process? scanServer;
    readonly List<object> scanPending=[];
    async void Scan_Click(object sender,RoutedEventArgs e){if(busy)return;OptionsOverlay.Visibility=Visibility.Collapsed;try{await ScanCatalog();}catch(Exception ex){Notify(ex.Message,Notice.Error);}}

    public async Task ScanCatalog(int maxItems=int.MaxValue,string pathFilter="") {
        if(busy||scanning)return;
        if(!ready)await LoadCatalog();
        if(!ready||availability==null)return;
        scanCancelled=false;
        var folder=System.IO.Path.Combine(root,"reports","catalog-scan");Directory.CreateDirectory(folder);
        using var scanLock=new FileStream(System.IO.Path.Combine(folder,"scan.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        var legacyLedger=System.IO.Path.Combine(folder,"results.jsonl");
        var ledger=System.IO.Path.Combine(root,"cache","scan-results.dat");
        if(File.Exists(legacyLedger))CompactDat.Migrate(legacyLedger,ledger,"records/legacy.jsonl");
        var state=System.IO.Path.Combine(root,"cache","scan-progress.dat");
        foreach(var legacyState in new[]{System.IO.Path.Combine(folder,"progress.json"),System.IO.Path.Combine(folder,"progress.json.tmp")})if(File.Exists(legacyState))CompactDat.Migrate(legacyState,state,System.IO.Path.GetFileName(legacyState));
        var paks=ResolvePaks(game);
        var mapping=System.IO.Path.Combine(root,"mappings","AION2-5.3.2.0-1.0.52.0.usmap");
        var scope=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(paks.ToUpperInvariant()+"|"+Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(mapping)))+"|scan-v1")));
        var sourceStamps=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        string Key(Asset asset) {
            if(!sourceStamps.TryGetValue(asset.Chunk,out var stamp)){
                string Stamp(string path){var file=new FileInfo(path);return file.Exists?$"{file.Length}:{file.LastWriteTimeUtc.Ticks}":"missing";}
                var path=System.IO.Path.Combine(paks,System.IO.Path.GetFileName(asset.Chunk));stamp=Stamp(path)+"|"+Stamp(System.IO.Path.ChangeExtension(path,"ucas"));sourceStamps[asset.Chunk]=stamp;
            }
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope+"|"+asset.Path.ToUpperInvariant()+"|"+asset.Size+"|"+asset.Chunk+"|"+stamp)));
        }
        var done=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(File.Exists(ledger))foreach(var line in CompactDat.Lines(ledger))try{using var record=JsonDocument.Parse(line);done.Add(record.RootElement.GetProperty("Key").GetString()!);}catch(JsonException){/* A torn last record is retried. */}
        int processed=0,skipped=0,failed=0,serverCount=0;
        scanning=true;ClosePreview();SetBusy(true);
        async Task SaveProgress(string status,string? path=null,string? error=null){
            
            try {
                var value=JsonSerializer.Serialize(new{Status=status,Total=all.Count,ProcessedThisRun=processed,Skipped=skipped,FailedThisRun=failed,CurrentAsset=path,Error=error,UpdatedUtc=DateTime.UtcNow});
                for(int attempt=0;;attempt++) {
                    try {CompactDat.Put(state,"progress.json",value);break;}
                    catch(Exception ex) when((ex is IOException or UnauthorizedAccessException)&&attempt<4){await Task.Delay(100*(attempt+1));}
                }
            }catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){
                // Status is informational. Preserve the ledger and continue if an editor locks this file.
                try{File.AppendAllText(System.IO.Path.Combine(folder,"errors.log"),$"{DateTime.UtcNow:O} Gravar {state}\n{ex}\n");}catch(IOException){}catch(UnauthorizedAccessException){}
            }
        }
        void Commit(){if(scanPending.Count==0)return;availability.Flush();CompactDat.AppendLines(ledger,scanPending.Select(item=>JsonSerializer.Serialize(item)));scanPending.Clear();}
        availability.DeferWrites=true;
        try {
            await SaveProgress("Running");
            foreach(var asset in all.Where(a=>a.Path.Contains(pathFilter,StringComparison.OrdinalIgnoreCase)).OrderBy(a=>a.Path,StringComparer.OrdinalIgnoreCase)) {
                if(scanCancelled||processed>=maxItems)break;
                var key=Key(asset);if(done.Contains(key)){skipped++;continue;}
                await SaveProgress("Running",asset.Path);
                Progress.IsIndeterminate=false;Progress.Value=100d*(processed+skipped)/Math.Max(1,all.Count);
                Notify($"Verificando tipos • {processed+skipped+1:N0}/{all.Count:N0}\n{asset.Path}");
                string outcome="Inspected";string? error=null;
                for(int cacheAttempt=0;cacheAttempt<2;cacheAttempt++){
                try {
                    if(asset.IsProbableAudio&&System.IO.Path.GetExtension(asset.Path).ToLowerInvariant() is ".wem" or ".wav" or ".ogg" or ".mp3" or ".opus" or ".binka") {
                        await ScanAudio(asset);
                    }else{
                        if(scanServer==null||scanServer.HasExited||serverCount>=100){StopScanServer();await StartScanServer(paks);serverCount=0;}
                        await scanServer!.StandardInput.WriteLineAsync(JsonSerializer.Serialize(asset.Path));await scanServer.StandardInput.FlushAsync();
                        using var response=JsonDocument.Parse(await ReadScanLine("SCAN_RESULT ",TimeSpan.FromMinutes(2)));
                        serverCount++;
                        if(response.RootElement.TryGetProperty("Error",out var failure))throw new InvalidDataException(failure.GetString());
                        using var inspection=JsonDocument.Parse(File.ReadAllText(response.RootElement.GetProperty("Inspection").GetString()!));
                        var data=inspection.RootElement;
                        if(response.RootElement.TryGetProperty("Animation",out var animation)&&animation.ValueKind==JsonValueKind.String) {
                            var clip=await Task.Run(()=>AnimationClipData.Load(animation.GetString()!));
                            using var panel=new AnimationPanel(clip);panel.ShowFrame(0);availability.ConfirmAnimation(asset,clip);
                        }else if(data.GetProperty("HasRootMesh").GetBoolean()) {
                            var models=data.GetProperty("Models").EnumerateArray().Select(m=>m.GetString()!).ToArray();
                            if(models.Length==0)throw new InvalidDataException("A malha não foi convertida para a prévia.");
                            await ShowPreview(asset,models,data.GetProperty("AssetRoot").GetString()!,0);
                            if(asset.Hint!=ModelAvailabilityCache.AvailableHint)throw new InvalidDataException("A geometria não foi confirmada no Viewer.");
                        }else if(data.GetProperty("Objects").EnumerateArray().Any(o=>(o.GetProperty("Type").GetString()??"").Contains("Texture",StringComparison.OrdinalIgnoreCase))) {
                            var image=data.GetProperty("Images").EnumerateArray().Select(i=>i.GetString()!).FirstOrDefault(File.Exists);
                            if(image==null)throw new InvalidDataException("A textura não forneceu uma imagem decodificável.");
                            availability.ConfirmTexture(asset,image);
                        }else if(asset.IsAudio||data.GetProperty("Objects").EnumerateArray().Any(o=>{
                            var type=o.GetProperty("Type").GetString()??"";return type.Contains("Sound",StringComparison.OrdinalIgnoreCase)||type.Contains("AkAudio",StringComparison.OrdinalIgnoreCase)||type.Contains("Wwise",StringComparison.OrdinalIgnoreCase);
                        }))await ScanAudio(asset);
                    }
                    if(asset.Hint is "Animação" or "Animação de Montagem" or "Áudio" or "Textura disponível" or "Modelo disponível")outcome="Confirmed";
                    break;
                }catch(Exception ex){
                    if(scanCancelled)break;
                    if(cacheAttempt==0&&IsCacheFailure(ex)){ResetAssetCache(asset);StopScanServer();continue;}
                    outcome="Failed";error=ex.Message;failed++;
                    if(asset.Hint!="Não classificado")availability.Reject(asset);
                    if(ex is TimeoutException||scanServer?.HasExited==true)StopScanServer();
                    break;
                }finally{
                    ClosePreview();previews.Clear();CleanupPreviews();CleanScanAudioReport();SetBusy(true);
                }
                }
                if(scanCancelled)break;
                scanPending.Add(new{Key=key,Scope=scope,asset.Path,asset.Size,asset.Chunk,Status=outcome,Type=asset.Hint,Error=error,CompletedUtc=DateTime.UtcNow});
                done.Add(key);processed++;
                if(scanPending.Count>=25)Commit();
                await SaveProgress("Running",asset.Path);
                await Task.Delay(1);
            }
            Commit();
            await SaveProgress(scanCancelled?"Interrupted":processed>=maxItems?"BatchComplete":"Completed");
            Notify($"Verificação {(scanCancelled?"interrompida":"concluída")} • {processed:N0} processados • {skipped:N0} pulados • {failed:N0} falhas. Retomada salva.",failed>0?Notice.Warning:Notice.Success);
        }catch(Exception ex){await SaveProgress("Stopped",error:ex.Message);Notify(ex.Message,Notice.Error);}
        finally{
            try{Commit();}catch(Exception ex){Notify("Falha ao gravar a retomada: "+ex.Message,Notice.Error);}
            availability.DeferWrites=false;StopScanServer();
            try{await Task.Run(()=>CompactDat.PackFiles(root,System.IO.Path.Combine(root,"cache","scan-support.dat"),Directory.EnumerateFiles(folder,"*",SearchOption.AllDirectories).Where(p=>System.IO.Path.GetFileName(p)!="scan.lock")));}catch(Exception ex){Notify("Não foi possível compactar o apoio da varredura: "+ex.Message,Notice.Warning);}
            scanning=false;SetBusy(false);RefreshHints();await ApplyFilter();
        }
    }
    async Task ScanAudio(Asset asset){
        var request=System.IO.Path.Combine(root,"reports","catalog-scan","audio-request.json");File.WriteAllText(request,JsonSerializer.Serialize(new[]{new{asset.Path}}));
        var work=await Work("audio",request);
        if(scanCancelled)throw new OperationCanceledException();
        using var result=JsonDocument.Parse(File.ReadAllText(work.Result));
        if(work.Code!=0||!result.RootElement.TryGetProperty("Tracks",out var tracks))throw new InvalidDataException(result.RootElement.TryGetProperty("Error",out var error)?error.GetString():"Áudio não decodificado.");
        await ShowAudio(asset,tracks.EnumerateArray().Select(t=>t.GetString()!).ToArray());
        if(!asset.AudioConfirmed)throw new InvalidDataException("O player não confirmou a mídia de áudio.");
        File.Delete(work.Result);File.Delete(System.IO.Path.ChangeExtension(work.Result,".log"));
    }
    async Task StartScanServer(string paks){
        var executable=System.IO.Path.Combine(root,"backend","Aion2Porting.Cli.exe");
        var info=new ProcessStartInfo(File.Exists(executable)?executable:System.IO.Path.Combine(root,"tools","dotnet","dotnet.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=root};
        if(!File.Exists(executable))info.ArgumentList.Add(System.IO.Path.Combine(root,"src","Aion2Porting.Cli","bin","Release","net10.0","Aion2Porting.Cli.dll"));
        info.ArgumentList.Add("scan-server");info.ArgumentList.Add("1");info.ArgumentList.Add("*");info.ArgumentList.Add("*");
        info.Environment["AION2_ROOT"]=root;info.Environment["AION2_GAME_DIR"]=paks;
        scanServer=Process.Start(info)??throw new IOException("Não foi possível iniciar a verificação.");
        scanServer.ErrorDataReceived+=(_,_)=>{};scanServer.BeginErrorReadLine();
        await ReadScanLine("SCAN_READY",TimeSpan.FromMinutes(2));
    }
    void CleanScanAudioReport(){
        var manifest=System.IO.Path.Combine(root,"reports","catalog-scan","audio-manifest.json");if(!File.Exists(manifest))return;
        try{using var json=JsonDocument.Parse(File.ReadAllText(manifest));var path=System.IO.Path.GetFullPath(json.RootElement.GetProperty("ReportDirectory").GetString()!);var parent=System.IO.Path.GetFullPath(System.IO.Path.Combine(root,"reports"));
            if(System.IO.Path.GetDirectoryName(path)==parent&&System.IO.Path.GetFileName(path).EndsWith("-audio",StringComparison.Ordinal)&&Directory.Exists(path))Directory.Delete(path,true);
            File.Delete(manifest);
        }catch(Exception ex){File.WriteAllText(System.IO.Path.Combine(root,"reports","catalog-scan","cleanup-error.txt"),ex.Message);}
    }
    async Task<string> ReadScanLine(string marker,TimeSpan timeout){
        var deadline=DateTime.UtcNow+timeout;
        while(true){
            if(scanCancelled)throw new OperationCanceledException();
            var remaining=deadline-DateTime.UtcNow;if(remaining<=TimeSpan.Zero)throw new TimeoutException("Tempo limite ao ler o asset.");
            var line=await scanServer!.StandardOutput.ReadLineAsync().WaitAsync(remaining);
            if(line==null)throw new IOException("O conversor encerrou antes de confirmar o asset.");
            if(line.StartsWith(marker,StringComparison.Ordinal))return line[marker.Length..];
        }
    }
    void StopScanServer(){if(scanServer==null)return;try{if(!scanServer.HasExited)scanServer.Kill(true);}catch{}scanServer.Dispose();scanServer=null;}
}
