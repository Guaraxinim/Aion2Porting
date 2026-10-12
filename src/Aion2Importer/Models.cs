using System.IO;
using System.Text.RegularExpressions;
namespace Aion2Importer;

public sealed class Catalog {
    public DateTime GeneratedAtUtc { get; set; }
    public List<Asset> Files { get; set; } = [];
}
public sealed class Asset : System.ComponentModel.INotifyPropertyChanged {
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public string Chunk { get; set; } = "";
    public string Name { get; private set; } = "";
    public string Category { get; private set; } = "";
    public string Section { get; private set; } = "";
    public string Hint { get; private set; } = "";
    public bool AudioConfirmed=>Hint=="Áudio";
    public bool TextureConfirmed=>Hint=="Textura disponível";
    public bool IsTexture=>Hint.StartsWith("Textura");
    public bool AnimationConfirmed=>Hint.StartsWith("Animação");
    public bool MontageConfirmed=>Hint=="Animação de Montagem";
    [System.Text.Json.Serialization.JsonIgnore] public SkeletonProfile? SkeletonProfile {get;set;}
    public void MarkAnimation(bool montage){Hint=montage?"Animação de Montagem":"Animação";SetThumbnail(null);PropertyChanged?.Invoke(this,new(nameof(Hint)));PropertyChanged?.Invoke(this,new(nameof(AnimationConfirmed)));PropertyChanged?.Invoke(this,new(nameof(MontageConfirmed)));}
    public void MarkTexture(){SetThumbnail(null);Hint="Textura disponível";PropertyChanged?.Invoke(this,new(nameof(Hint)));PropertyChanged?.Invoke(this,new(nameof(TextureConfirmed)));PropertyChanged?.Invoke(this,new(nameof(IsTexture)));}
    public bool IsEffect=>Path.Contains("/FX/",StringComparison.OrdinalIgnoreCase);
    public bool IsAudio => AudioConfirmed||IsProbableAudio||Path.Contains("/Sound/",StringComparison.OrdinalIgnoreCase)||Path.Contains("/Wwise",StringComparison.OrdinalIgnoreCase);
    public bool IsProbableAudio=>Path.Contains("/WwiseAudio/Events/",StringComparison.OrdinalIgnoreCase)||
        new[]{".wem",".wav",".ogg",".mp3",".opus",".binka"}.Contains(System.IO.Path.GetExtension(Path),StringComparer.OrdinalIgnoreCase)||
        Name.StartsWith("SW_",StringComparison.OrdinalIgnoreCase);
    public void MarkAudio(){Hint="Áudio";SetThumbnail(null);PropertyChanged?.Invoke(this,new(nameof(Hint)));PropertyChanged?.Invoke(this,new(nameof(AudioConfirmed)));PropertyChanged?.Invoke(this,new(nameof(IsAudio)));}
    public string SizeText => Size >= 1048576 ? $"{Size/1048576d:0.0} MB" : $"{Size/1024d:0.0} KB";
    public bool SupersededType { get; set; }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public void MarkModelAvailable() { Hint=ModelAvailabilityCache.AvailableHint; PropertyChanged?.Invoke(this,new(nameof(Hint))); }
    [System.Text.Json.Serialization.JsonIgnore]
    public System.Windows.Media.Imaging.BitmapSource? Thumbnail {get;private set;}
    public void SetThumbnail(System.Windows.Media.Imaging.BitmapSource? image) {Thumbnail=image;PropertyChanged?.Invoke(this,new(nameof(Thumbnail)));}
    public void MarkUnclassified() {Hint="Não classificado";SetThumbnail(null);PropertyChanged?.Invoke(this,new(nameof(Hint)));PropertyChanged?.Invoke(this,new(nameof(AudioConfirmed)));}
    public void SetSection(string section) {Section=section;PropertyChanged?.Invoke(this,new(nameof(Section)));}
    public void Prepare() {
        Name = System.IO.Path.GetFileNameWithoutExtension(Path);
        var p = Path.ToLowerInvariant();
        Category = p.Contains("/character/player/") ? "Jogadores" :
            p.Contains("/character/monster") ? "Monstros" :
            p.Contains("/character/npc/") ? "NPCs" :
            p.Contains("/character/pet/") ? "Pets" :
            p.Contains("/character/") ? "Outros personagens" :
            p.Contains("/content/bg/") || p.Contains("/content/envobj/") ? "Cenários e objetos" :
            p.Contains("/content/map") ? "Mapas" :
            p.Contains("/content/fx/") ? "Efeitos" :
            p.Contains("/content/ui") ? "Interface" :
            p.Contains("/content/cinematic/") ? "Cinemáticas" :
            p.Contains("/sound/") || p.Contains("/wwise") ? "Áudio" : "Outros recursos";
        Section = p.Contains("/hair/") ? "Cabelos" : p.Contains("/headwear/") ? "Adereços de cabeça" :
            p.Contains("/shoulderwear/") ? "Adereços de ombro" : p.Contains("/weapon/") ? "Armas" :
            p.Contains("/face/") || p.Contains("/head/") ? "Rostos e cabeças" :
            p.Contains("/body/") ? "Corpos" : p.Contains("/customize/") ? "Personalização" :
            p.Contains("/common/") ? "Compartilhados" : "Outros";
        var n=Name.ToUpperInvariant();
        Hint = IsProbableAudio ? "Áudio provável" : n.StartsWith("MI_") || n.StartsWith("M_") || n.StartsWith("MM_") ? "Material (nome)" :
            n.StartsWith("T_") || Regex.IsMatch(n, @"_(D|N|ARM|DO|DOI|SE|CSTM)$") ? "Textura (nome)" :
            n.StartsWith("SK_") ? "Esqueleto (nome)" :
            n.StartsWith("ABP_") || n.StartsWith("AN_") || n.EndsWith("_ANIM") ? "Animação provável" :
            n.StartsWith("BP_") || n.StartsWith("CR_") || n.StartsWith("PA_") ? "Controle (nome)" :
            Regex.IsMatch(n, @"^(GM|GF)_HAIR_\d+(_TYPE\d+)?$") || n.StartsWith("SM_") || n.StartsWith("HW_") || n.StartsWith("SDW_") ? "Modelo provável" : "Não classificado";
    }
}
public record CategoryChoice(string Name,int Count) { public string Caption => $"{Name}   {Count:N0}"; }
public sealed class QueueItem : System.ComponentModel.INotifyPropertyChanged {
    public required Asset Asset { get; init; }
    public string? AnimationClip { get; init; }
    public string? AnimationSlot { get; init; }
    public string? ModelFile { get; init; }
    public string? AssetRoot { get; init; }
    public bool IsAnimation => AnimationClip!=null;
    public string Name => IsAnimation ? Asset.Name+(AnimationSlot is null or "Sequence" or "DefaultSlot"?"":" • "+AnimationSlot)+(ModelFile==null?" • esqueleto + animação":" • modelo + texturas + animação") : Asset.Name;
    public string Path => Asset.Path;
    string status="Na fila", output="", error="";
    public string Status { get => status; set { status=value; Changed(nameof(Status)); } }
    public string Output { get=>output; set {output=value; Changed(nameof(Output));} }
    public string Error { get=>error; set {error=value; Changed(nameof(Error));} }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    void Changed(string n)=>PropertyChanged?.Invoke(this,new(n));
}
public static class SearchEngine {
    public static void Prepare(IReadOnlyList<Asset> assets) {
        var bases = assets.Select(a=>{a.Prepare(); return a.Name;}).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var a in assets) {
            var match=Regex.Match(a.Name,@"^(.*)_Type\d+$",RegexOptions.IgnoreCase);
            a.SupersededType=match.Success && bases.Contains(match.Groups[1].Value);
        }
    }
    public static List<Asset> Filter(IEnumerable<Asset> source,string query,string mode,string folder,string category,string section,string hint,string chunk,bool preferBase,IReadOnlySet<string>? hints=null,string exclude="") {
        Regex? pattern=mode=="Padrão (* e ?)" && query.Length>0 ? new Regex("^"+Regex.Escape(query).Replace(@"\*",".*").Replace(@"\?",".")+"$",RegexOptions.IgnoreCase,TimeSpan.FromMilliseconds(100)) : null;
        var tokens=query.Split(' ',StringSplitOptions.RemoveEmptyEntries);
        var excluded=exclude.Split(new[]{';',',','\r','\n'},StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        return source.Where(a=>
            (category=="Todos os assets"||a.Category==category) &&
            (section=="Todas as subcategorias"||a.Section==section) &&
            (hints!=null ? hints.Count==0||hints.Contains("Todos os tipos")||hints.Contains(a.Hint) : hint=="Todos os tipos"||a.Hint==hint) &&
            !excluded.Any(t=>a.Path.Contains(t,StringComparison.OrdinalIgnoreCase)) &&
            (chunk=="Todos os chunks"||a.Chunk==chunk) &&
            (!preferBase||!a.SupersededType) &&
            (folder.Length==0||a.Path.StartsWith(folder.Replace('\\','/'),StringComparison.OrdinalIgnoreCase)) &&
            (query.Length==0|| (pattern!=null ? pattern.IsMatch(a.Name)||pattern.IsMatch(System.IO.Path.GetFileName(a.Path)) :
            mode=="Caminho exato" ? a.Path.Equals(query.Replace((char)92,'/'),StringComparison.OrdinalIgnoreCase) :
            mode=="Apenas nome do asset" ? tokens.All(t=>a.Name.Contains(t,StringComparison.OrdinalIgnoreCase)) :
            mode=="Nome exato" ? a.Name.Equals(query,StringComparison.OrdinalIgnoreCase)||System.IO.Path.GetFileName(a.Path).Equals(query,StringComparison.OrdinalIgnoreCase) :
            tokens.All(t=>a.Path.Contains(t,StringComparison.OrdinalIgnoreCase))))
        ).ToList();
    }
}
