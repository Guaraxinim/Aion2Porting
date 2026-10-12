using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Aion2Importer;
public static class CompactDat {
    public static T Read<T>(string file,string entry,Func<Stream,T> read){using var zip=ZipFile.OpenRead(file);using var input=(zip.GetEntry(entry)??throw new InvalidDataException("Entrada ausente no DAT: "+entry)).Open();return read(input);}
    public static void Update(string file,Action<ZipArchive> action){
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);var temporary=file+".tmp.dat";
        try{
            if(File.Exists(file))File.Copy(file,temporary,true);else if(File.Exists(temporary))File.Delete(temporary);
            using(var zip=ZipFile.Open(temporary,ZipArchiveMode.Update))action(zip);
            File.Move(temporary,file,true);
        }finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    public static void Put(string file,string entry,string value)=>Update(file,zip=>{zip.GetEntry(entry)?.Delete();using var writer=new StreamWriter(zip.CreateEntry(entry,CompressionLevel.Optimal).Open(),new UTF8Encoding(false));writer.Write(value);});
    public static void Migrate(string source,string file,string entry){
        if(!File.Exists(source))return;
        Update(file,zip=>{zip.GetEntry(entry)?.Delete();using var input=File.OpenRead(source);using var output=zip.CreateEntry(entry,CompressionLevel.Optimal).Open();input.CopyTo(output);});
        // Verify readable contents before removing the original.
        Read(file,entry,input=>{input.CopyTo(Stream.Null);return true;});try{File.Delete(source);}catch(IOException){}catch(UnauthorizedAccessException){}
    }
    public static IEnumerable<string> Lines(string file){
        if(!File.Exists(file))yield break;
        using var zip=ZipFile.OpenRead(file);
        foreach(var entry in zip.Entries.OrderBy(e=>e.FullName,StringComparer.Ordinal)){
            using var reader=new StreamReader(entry.Open());while(reader.ReadLine() is string line)yield return line;
        }
    }
    public static void AppendLines(string file,IEnumerable<string> lines)=>Put(file,"records/"+DateTime.UtcNow.Ticks.ToString("D19")+"-"+Guid.NewGuid().ToString("N")+".jsonl",string.Join("\n",lines)+"\n");
    public static void PackFiles(string root,string file,IEnumerable<string> paths){
        root=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        var files=paths.Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists).Select(Path.GetFullPath).ToArray();
        foreach(var path in files){
            if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase)||path.Equals(file,StringComparison.OrdinalIgnoreCase))throw new IOException("Arquivo fora da pasta temporária autorizada.");
            for(var info=new FileInfo(path) as FileSystemInfo;info!=null;info=info is FileInfo f?f.Directory:(info as DirectoryInfo)?.Parent){if((info.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("A compactação não segue atalhos.");if(info.FullName.TrimEnd(Path.DirectorySeparatorChar)==root.TrimEnd(Path.DirectorySeparatorChar))break;}
        }
        if(files.Length==0)return;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temporary=file+".tmp.dat";
        var replacements=files.Select(p=>Path.GetRelativePath(root,p).Replace('\\','/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        try{
            if(File.Exists(temporary))File.Delete(temporary);
            // Create mode streams large audio/GLB files to disk rather than retaining all entries in RAM.
            using(var zip=ZipFile.Open(temporary,ZipArchiveMode.Create)){
                if(File.Exists(file)){using var previous=ZipFile.OpenRead(file);foreach(var entry in previous.Entries.Where(e=>!replacements.Contains(e.FullName))){using var input=entry.Open();using var output=zip.CreateEntry(entry.FullName,CompressionLevel.Optimal).Open();input.CopyTo(output);}}
                foreach(var path in files){using var input=File.OpenRead(path);using var output=zip.CreateEntry(Path.GetRelativePath(root,path).Replace('\\','/'),CompressionLevel.Optimal).Open();input.CopyTo(output);}
            }
            using(var zip=ZipFile.OpenRead(temporary))foreach(var path in files){var entry=zip.GetEntry(Path.GetRelativePath(root,path).Replace('\\','/'))??throw new IOException("Arquivo não confirmado no DAT.");if(entry.Length!=new FileInfo(path).Length)throw new IOException("Tamanho não confirmado no DAT.");using var input=entry.Open();using var source=File.OpenRead(path);if(!System.Security.Cryptography.SHA256.HashData(input).SequenceEqual(System.Security.Cryptography.SHA256.HashData(source)))throw new IOException("Conteúdo não confirmado no DAT.");}
            File.Move(temporary,file,true);
        }finally{if(File.Exists(temporary))File.Delete(temporary);}
        foreach(var path in files)File.Delete(path);
    }
}
