using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
namespace Aion2Importer;

// A downloaded executable owns immutable, versioned support files and mutable user data separately.
public static class ReleaseRuntime {
    public static string? SupportRoot {get;private set;}
    public static bool Bundled {get;private set;}
    static FileStream? sessionLock;
    public static string? Initialize() {
        using var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("Aion2Importer.Runtime.dat");
        if(resource==null)return null;
        Bundled=true;
        var data=Path.GetFullPath(Environment.GetEnvironmentVariable("AION2_DATA_HOME")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Aion2Porting"));
        Directory.CreateDirectory(data);
        // One packaged instance per data directory prevents cleanup while another viewer is using previews.
        sessionLock=new FileStream(Path.Combine(data,"session.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        var version=Assembly.GetExecutingAssembly().GetName().Version!.ToString(3);
        var digest=Convert.ToHexString(SHA256.HashData(resource));resource.Position=0;
        SupportRoot=Path.Combine(data,"runtime",version+"-"+digest[..12]);
        var marker=Path.Combine(SupportRoot,"complete.dat");
        if(!File.Exists(marker)||File.ReadAllText(marker)!=digest) {
            Directory.CreateDirectory(SupportRoot);
            using var zip=new ZipArchive(resource,ZipArchiveMode.Read);
            foreach(var entry in zip.Entries) {
                var target=Path.GetFullPath(Path.Combine(SupportRoot,entry.FullName));
                if(!target.StartsWith(SupportRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Invalid bundled path.");
                if(entry.FullName.EndsWith('/')){Directory.CreateDirectory(target);continue;}
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target,true);
            }
            File.WriteAllText(marker,digest);
        }
        Directory.CreateDirectory(Path.Combine(data,"cache"));
        var catalog=Path.Combine(data,"cache","catalog.dat");
        if(!File.Exists(catalog))File.Copy(Path.Combine(SupportRoot,"seed","catalog.dat"),catalog);
        return data;
    }
    public static string Support(string root,params string[] parts)=>Path.Combine(new[]{SupportRoot??root}.Concat(parts).ToArray());
}
