using System.IO;
using System.Text.Json;
namespace Aion2Importer;
public partial class MainWindow {
    async Task VerifyCompactStorage(){
        var directory=System.IO.Path.Combine(root,"reports","dat-storage-test");Directory.CreateDirectory(directory);
        var source=System.IO.Path.Combine(directory,"source.txt");var dat=System.IO.Path.Combine(directory,"storage.dat");
        if(File.Exists(dat))File.Delete(dat);
        var payload=new string('a',100000);File.WriteAllText(source,payload);
        CompactDat.PackFiles(directory,dat,new[]{source});
        if(File.Exists(source)||CompactDat.Read(dat,"source.txt",input=>new StreamReader(input).ReadToEnd())!=payload||new FileInfo(dat).Length>=payload.Length/10)throw new Exception("DAT compact round trip failed");
        var ledger=System.IO.Path.Combine(directory,"resume.dat");if(File.Exists(ledger))File.Delete(ledger);
        CompactDat.AppendLines(ledger,new[]{"{\"Key\":\"one\"}"});CompactDat.AppendLines(ledger,new[]{"{\"Key\":\"two\"}"});
        if(CompactDat.Lines(ledger).Count()!=2)throw new Exception("DAT resumable ledger failed");
        var asset=new Asset{Path="fixture.uasset",Size=1,Chunk="fixture.utoc"};asset.Prepare();
        var cacheFile=System.IO.Path.Combine(directory,"asset.json");var cacheDat=System.IO.Path.ChangeExtension(cacheFile,"dat");if(File.Exists(cacheDat))File.Delete(cacheDat);
        var cache=new ModelAvailabilityCache(cacheFile,directory,"missing");cache.SetSubcategory(new[]{asset},"Minha categoria");cache.Reject(asset);cache.Invalidate(asset);asset.Prepare();cache.Apply(new[]{asset});
        if(asset.Section!="Minha categoria")throw new Exception("Recovery lost a manual subcategory");
        File.WriteAllText(cacheDat,"broken archive");var corrupt=new ModelAvailabilityCache(cacheFile,directory,"missing");corrupt.Invalidate(asset);
        if(corrupt.Warning!=null)throw new Exception("Unreadable DAT was not recovered");
        var previous=availability;availability=cache;int attempts=0;
        try{
            var recovered=await RecoverAssetCache(asset,new FileNotFoundException("Missing cache"),async()=>{attempts++;if(await RecoverAssetCache(asset,new FileNotFoundException("Missing cache"),()=>Task.CompletedTask))throw new Exception("Retry loop was not bounded");});
            if(!recovered||attempts!=1)throw new Exception("Cache retry was not performed exactly once");
        }finally{availability=previous;SetBusy(false);}
        CompactDat.Put(System.IO.Path.Combine(directory,"validation.dat"),"result.json",JsonSerializer.Serialize(new{Passed=true,CompactRoundTrip=true,ResumableLedger=true,ManualSubcategoryPreserved=true,CorruptDatRecovered=true,RetryBounded=true}));
    }
}
