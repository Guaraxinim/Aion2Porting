using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Aion2Importer;
public sealed class AudioPanel : UserControl, IDisposable {
    readonly MediaElement player=new(){LoadedBehavior=MediaState.Manual,UnloadedBehavior=MediaState.Manual,Volume=.35,Height=0};
    readonly ComboBox tracks=new(){Margin=new Thickness(0,16,0,12)};
    readonly Slider progress=new(){Minimum=0,Maximum=1,Margin=new Thickness(0,14,0,4)};
    readonly TextBlock time=new(){Foreground=Brushes.LightGray};
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(250)};
    TaskCompletionSource<bool>? opened;
    bool disposed,updating;
    public event Action? CloseRequested;
    public event Action<string>? Failed;
    public event Action? Ready;
    public event Action? InspectRequested;
    public TimeSpan Position=>player.Position;
    public TimeSpan Duration=>player.NaturalDuration.HasTimeSpan?player.NaturalDuration.TimeSpan:TimeSpan.Zero;
    public AudioPanel(string name,bool muted=false) {
        Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/Aion2Porting;component/Design.xaml",UriKind.Relative)});
        Background=new SolidColorBrush(Color.FromRgb(18,22,39));
        if(muted)player.Volume=0;
        var layout=new StackPanel{Margin=new Thickness(16)};
        var close=new Button{Content="Fechar áudio",HorizontalAlignment=HorizontalAlignment.Right};
        close.Click+=(_,_)=>CloseRequested?.Invoke();layout.Children.Add(close);
        layout.Children.Add(new TextBlock{Text="PRÉVIA DE ÁUDIO",Foreground=new SolidColorBrush(Color.FromRgb(0,242,254)),FontWeight=FontWeights.Bold,Margin=new Thickness(0,12,0,8)});
        var header=new DockPanel();var info=new Button{Content="ⓘ",ToolTip="Inspecionar asset"};DockPanel.SetDock(info,Dock.Right);info.Click+=(_,_)=>InspectRequested?.Invoke();header.Children.Add(info);header.Children.Add(new TextBlock{Text=name,Foreground=Brushes.White,TextWrapping=TextWrapping.Wrap});layout.Children.Add(header);
        layout.Children.Add(tracks);
        var buttons=new WrapPanel();
        void Button(string caption,Action action) {
            var b=new Button{Content=caption,Padding=new Thickness(9,7,9,7)};b.Click+=(_,_)=>action();buttons.Children.Add(b);
        }
        Button("▶ Reproduzir",()=>player.Play());Button("Ⅱ Pausar",()=>player.Pause());Button("■ Parar",()=>{player.Stop();player.Position=TimeSpan.Zero;});
        layout.Children.Add(buttons);layout.Children.Add(progress);layout.Children.Add(time);
        layout.Children.Add(new TextBlock{Text="Volume",Foreground=Brushes.LightGray,Margin=new Thickness(0,16,0,4)});
        var volume=new Slider{Minimum=0,Maximum=1,Value=player.Volume};volume.ValueChanged+=(_,_)=>player.Volume=volume.Value;layout.Children.Add(volume);
        layout.Children.Add(new TextBlock{Text="Faixas de mídia do evento. Combinações, efeitos e condições do Wwise não são simulados.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.Gray,Margin=new Thickness(0,18,0,0)});
        layout.Children.Add(player);Content=layout;
        player.MediaOpened+=(_,_)=>{progress.Maximum=Math.Max(.001,Duration.TotalSeconds);opened?.TrySetResult(true);Ready?.Invoke();};
        player.MediaFailed+=(_,e)=>{opened?.TrySetException(e.ErrorException);Failed?.Invoke(e.ErrorException.Message);};
        player.MediaEnded+=(_,_)=>{player.Stop();player.Position=TimeSpan.Zero;};
        progress.ValueChanged+=(_,_)=>{if(!updating&&Duration>TimeSpan.Zero)player.Position=TimeSpan.FromSeconds(progress.Value);};
        timer.Tick+=(_,_)=>{
            if(disposed)return;
            updating=true;if(!progress.IsMouseCaptureWithin)progress.Value=Math.Min(progress.Maximum,player.Position.TotalSeconds);updating=false;
            time.Text=$"{player.Position:mm\\:ss} / {Duration:mm\\:ss}";
        };
        tracks.SelectionChanged+=async(_,_)=>{
            if(tracks.SelectedItem is not Track track||disposed)return;
            try{await PlayFile(track.Path);}catch(Exception ex){if(!disposed)Failed?.Invoke(ex.Message);}
        };
        timer.Start();
    }
    sealed record Track(string Path,string Label);
    public async Task OpenAsync(string[] files) {
        if(files.Length==0)throw new InvalidDataException("Nenhuma faixa de áudio.");
        tracks.DisplayMemberPath=nameof(Track.Label);
        tracks.ItemsSource=files.Select((path,index)=>new Track(path,$"Faixa {index+1} • {Path.GetFileNameWithoutExtension(path)}")).ToArray();
        tracks.Visibility=files.Length>1?Visibility.Visible:Visibility.Collapsed;
        await PlayFile(files[0]);
    }
    async Task PlayFile(string file) {
        player.Close();opened?.TrySetCanceled();
        var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);opened=completion;
        player.Source=new Uri(Path.GetFullPath(file));player.Play();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }
    public void Dispose() {
        disposed=true;timer.Stop();opened?.TrySetCanceled();player.Close();
    }
}
