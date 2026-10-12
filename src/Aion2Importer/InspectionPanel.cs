using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Aion2Importer;
public sealed class InspectionPanel : UserControl,IDisposable {
    PreviewPanel? preview;
    public event Action? CloseRequested;
    public event Action<string>? Failed;
    public int ImageCount {get;private set;}
    public int ObjectCount {get;private set;}
    public InspectionPanel(string name,JsonElement data) {
        Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/Aion2Porting;component/Design.xaml",UriKind.Relative)});
        Background=new SolidColorBrush(Color.FromRgb(18,22,39));
        var body=new StackPanel{Margin=new Thickness(14)};
        Content=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        void Text(string value,bool heading=false) {body.Children.Add(new TextBlock{Text=value,TextWrapping=TextWrapping.Wrap,Foreground=heading?Brushes.Cyan:Brushes.LightGray,FontWeight=heading?FontWeights.Bold:FontWeights.Normal,Margin=new Thickness(0,8,0,5)});}
        var close=new Button{Content="Fechar inspeção",HorizontalAlignment=HorizontalAlignment.Right};close.Click+=(_,_)=>CloseRequested?.Invoke();body.Children.Add(close);
        Text(name,true);Text("INSPEÇÃO DO ASSET",true);
        Text("Esta leitura mostra recursos e parâmetros. Não reproduz a animação nem os shaders do efeito no jogo.");
        var objects=data.GetProperty("Objects").EnumerateArray().ToArray();ObjectCount=objects.Length;
        var types=objects.Select(o=>o.GetProperty("Type").GetString()??"").ToArray();
        int Count(string type)=>types.Count(t=>t.Contains(type,StringComparison.OrdinalIgnoreCase));
        var emitters=Count("Emitter");var sprites=Count("SpriteRenderer");var ribbons=Count("RibbonRenderer");var meshes=Count("MeshRenderer");
        if(types.Any(t=>t.Contains("Niagara")))Text($"Partículas Niagara • {emitters} emissores\nDesenho por imagens: {sprites} • fitas/rastros: {ribbons} • malhas: {meshes}",true);
        else Text(string.Join(" • ",types.Distinct().Take(6)),true);
        var models=data.GetProperty("Models").EnumerateArray().Select(v=>v.GetString()!).ToArray();
        if(models.Length>0) {
            Text("Malhas associadas • geometria isolada",true);
            preview=new PreviewPanel(name,models,data.GetProperty("AssetRoot").GetString()!,()=>{});
            preview.HideQueueAction();preview.Height=460;preview.ModelFailed+=message=>Failed?.Invoke(message);
            preview.CloseRequested+=()=>{preview.DisposePreview();preview.Visibility=Visibility.Collapsed;};
            body.Children.Add(preview);
            Loaded+=async(_,_)=>{try{await preview.OpenAsync();}catch(Exception ex){Failed?.Invoke(ex.Message);}};
        }
        foreach(var path in data.GetProperty("Images").EnumerateArray().Select(v=>v.GetString()!)) {
            try {
                using var stream=File.OpenRead(path);
                var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=512;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();
                Text(Path.GetFileNameWithoutExtension(path));
                body.Children.Add(new Border{Background=new SolidColorBrush(Color.FromRgb(48,52,68)),Padding=new Thickness(8),Child=new Image{Source=bitmap,Stretch=Stretch.Uniform,MaxHeight=300}});
                ImageCount++;
            }catch(Exception ex){Text("Imagem indisponível: "+ex.Message);}
        }
        Text("Componentes e parâmetros",true);
        foreach(var obj in objects) {
            var props=new StackPanel();
            foreach(var prop in obj.GetProperty("Properties").EnumerateArray()) {
                var key=prop.GetProperty("Name").GetString()??"";
                var label=key switch {"bEnabled"=>"Ativo","EmitterHandles"=>"Emissores","FixedBounds"=>"Limites espaciais","Material"=>"Material","Meshes"=>"Malhas","TextureParameterValues"=>"Texturas","ScalarParameterValues"=>"Valores numéricos","VectorParameterValues"=>"Cores/vetores","bAutoDeactivate"=>"Desativação automática","Duration"=>"Duração","LoopDuration"=>"Duração do ciclo",_=>key};
                props.Children.Add(new TextBlock{Text=label+": "+prop.GetProperty("Value").GetString(),Foreground=Brushes.LightGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(5),MaxHeight=180,ToolTip=key});
            }
            body.Children.Add(new Expander{Header=obj.GetProperty("Type").GetString()+" • "+obj.GetProperty("Name").GetString(),Content=props,Foreground=Brushes.LightGray,Margin=new Thickness(0,5,0,0)});
        }
        Text("Recursos referenciados",true);
        foreach(var path in data.GetProperty("References").EnumerateArray().Take(80))Text(path.GetString()??"");
        if(data.GetProperty("Limited").GetBoolean())Text("A inspeção foi limitada para manter a prévia leve.");
        foreach(var warning in data.GetProperty("Warnings").EnumerateArray())Text("Leitura parcial: "+warning.GetString());
    }
    public void Dispose()=>preview?.DisposePreview();
}
