using System.IO;
namespace Aion2Importer;
public static class PreviewCacheCleanup {
    public static bool OtherInstance(string root){
        root=Path.GetFullPath(root);
        // Another window may still be converting or displaying files in the shared preview cache.
        foreach(var process in System.Diagnostics.Process.GetProcessesByName("Aion2Porting"))using(process){
            if(process.Id==Environment.ProcessId)continue;
            try{var executable=process.MainModule?.FileName;if(executable!=null&&Path.GetFullPath(executable).StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))return true;}catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}
        }
        return false;
    }
    public static void Clear(string root) {
        root=Path.GetFullPath(root);
        if(OtherInstance(root))return;
        var cache=Path.GetFullPath(Path.Combine(root,"cache"));
        var target=Path.GetFullPath(Path.Combine(cache,"previews"));
        if(!target.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            throw new IOException("Cache fora da pasta do aplicativo.");
        if(!Directory.Exists(target))return;
        if((File.GetAttributes(cache)&FileAttributes.ReparsePoint)!=0||(File.GetAttributes(target)&FileAttributes.ReparsePoint)!=0)
            throw new IOException("A limpeza não segue atalhos de diretório.");
        void Delete(DirectoryInfo directory) {
            foreach(var entry in directory.EnumerateFileSystemInfos()) {
                if(entry is DirectoryInfo child) {
                    if((child.Attributes&FileAttributes.ReparsePoint)!=0)child.Delete();
                    else Delete(child);
                } else entry.Delete();
            }
            directory.Delete();
        }
        Delete(new DirectoryInfo(target));
    }
}
