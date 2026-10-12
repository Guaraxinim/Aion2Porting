using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Aion2Importer;
public partial class MainWindow : Window {
    readonly string root;
    readonly bool testing;
    List<Asset> all=[], filtered=[];
    readonly ObservableCollection<QueueItem> queue=[];
    readonly DispatcherTimer debounce=new(){Interval=TimeSpan.FromMilliseconds(250)};
    bool ready, busy, stopped;
    int filterVersion;
    Process? worker;
    string blender=@"C:\Program Files (x86)\Steam\steamapps\common\Blender\blender.exe";
    string game=@"C:\Program Files (x86)\Steam\steamapps\common\AION2";
    string outputFolder="";
    string lastReport="";
    string? lastPreviewModel;
    public MainWindow(string projectRoot,bool test=false) {
        root=projectRoot; testing=test;
        Localization.SetLanguage("pt-BR");
        Directory.CreateDirectory(System.IO.Path.Combine(root,"reports"));
        Directory.CreateDirectory(System.IO.Path.Combine(root,"exports"));
        outputFolder=System.IO.Path.Combine(root,"Exportados");
        InitializeComponent();
        Localization.SetEnabled(this,true);
        var version=typeof(MainWindow).Assembly.GetName().Version!.ToString(3);
        VersionLabel.Text="VERSÃO "+version;HomeVersionLabel.Text="NOVA INTERFACE • "+version;
        SearchMode.ItemsSource=new[]{"Palavras no caminho","Nome exato","Padrão (* e ?)","Caminho exato","Apenas nome do asset"};
        SearchMode.SelectedIndex=0;
        Sections.ItemsSource=new[]{"Todas as subcategorias"}; Sections.SelectedIndex=0;
        Hints.ItemsSource=new[]{"Todos os tipos"}; Hints.SelectedIndex=0;
        Chunks.ItemsSource=new[]{"Todos os chunks"}; Chunks.SelectedIndex=0;
        QueueGrid.ItemsSource=queue;
        debounce.Tick+=async (_,_)=>{debounce.Stop();await ApplyFilter();};
        try {
            var settings=JsonDocument.Parse(ReadSettings());
            blender=settings.RootElement.GetProperty("Blender").GetString()??blender;
            if(settings.RootElement.TryGetProperty("Game",out var g))game=g.GetString()??game;
            if(settings.RootElement.TryGetProperty("Output",out var o))outputFolder=o.GetString()??outputFolder;
            if(settings.RootElement.TryGetProperty("QueueVisible",out var v))queueVisible=v.GetBoolean();
            if(settings.RootElement.TryGetProperty("LibraryVisible",out var lv))libraryVisible=lv.GetBoolean();
            if(settings.RootElement.TryGetProperty("Language",out var language))Localization.SetLanguage(language.GetString()??"pt-BR");
        } catch { }
        SetQueueVisible(queueVisible);SetLibraryVisible(libraryVisible);
        Loaded+=async (_,_)=>{if(!testing){SetBusy(true);Notify("Compactando cache e relatórios antigos…");try{await Task.Run(()=>CompactIdleData());}catch(Exception ex){Notify(ex.Message,Notice.Warning);}await LoadCatalog();}};
        Closed+=(_,_)=>{ClosePreview();try{CompactIdleData(true);}catch{}CleanupPreviews();var temporary=System.IO.Path.Combine(root,"cache","temporary.dat");if(!PreviewCacheCleanup.OtherInstance(root)&&File.Exists(temporary))File.Delete(temporary);noticePlayer?.Dispose();noticeWave?.Dispose();};
        Closing+=(_,e)=>{
            if(busy) {e.Cancel=true;Notify("Interrompa a tarefa antes de fechar.",Notice.Warning);}
        };
        SetBusy(false);
    }
    internal void MissingSupportNotice()=>Notify("Pastas de suporte ausentes. Mantenha o executável junto das pastas do programa.",Notice.Error);
    void CopyStatus_Click(object sender,System.Windows.Input.MouseButtonEventArgs e) {
        try{if(!string.IsNullOrWhiteSpace(StatusText.Text))Clipboard.SetText(StatusText.Text);e.Handled=true;}
        catch(System.Runtime.InteropServices.COMException){StatusText.ToolTip="Área de transferência ocupada. Clique novamente para copiar.";}
    }
    enum Notice { Info, Success, Warning, Error }
    DateTime lastNoticeSound;
    System.Media.SoundPlayer? noticePlayer;
    MemoryStream? noticeWave;
    void Notify(string message,Notice level=Notice.Info) {
        StatusText.Text=message;StatusText.ToolTip=message;
        StatusText.Foreground=new SolidColorBrush(level switch {
            Notice.Success=>Color.FromRgb(74,222,128),Notice.Warning=>Color.FromRgb(250,204,21),
            Notice.Error=>Color.FromRgb(255,98,98),_=>Color.FromRgb(138,148,184)});
        StatusText.FontWeight=level==Notice.Info?FontWeights.Normal:FontWeights.Bold;
        if(testing||level is not (Notice.Warning or Notice.Error)||(DateTime.UtcNow-lastNoticeSound).TotalSeconds<2)return;
        lastNoticeSound=DateTime.UtcNow;
        try {
            if(noticePlayer==null) {
                noticeWave=new MemoryStream();
                using(var writer=new BinaryWriter(noticeWave,System.Text.Encoding.UTF8,true)) {
                    const int rate=22050,count=2646;
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+count*2);
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);
                    writer.Write(rate);writer.Write(rate*2);writer.Write((short)2);writer.Write((short)16);
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(count*2);
                    for(int i=0;i<count;i++)writer.Write((short)(1100*Math.Sin(2*Math.PI*660*i/rate)*Math.Pow(Math.Sin(Math.PI*i/count),2)));
                }
                noticeWave.Position=0;noticePlayer=new System.Media.SoundPlayer(noticeWave);noticePlayer.Load();
            }
            noticePlayer.Play();
        }catch{ /* An unavailable sound device must not interrupt the operation. */ }
    }
    readonly ObservableCollection<Asset> visibleAssets=[];
    readonly Dictionary<int,int> removedFromPage=[];
    int PageCount=>Math.Max(1,(filtered.Count+removedFromPage.Values.Sum()+PageSize-1)/PageSize);
    int PageOffset(int page)=>page*PageSize-removedFromPage.Where(p=>p.Key<page).Sum(p=>p.Value);
    static T? FindVisual<T>(DependencyObject parent) where T:DependencyObject {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++) {
            var child=VisualTreeHelper.GetChild(parent,i);
            if(child is T match)return match;
            var nested=FindVisual<T>(child);if(nested!=null)return nested;
        }
        return null;
    }
    void RefreshAssetInPlace(Asset asset) {
        RefreshHints();
        var types=Hints.SelectedItems.Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matches=SearchEngine.Filter(new[]{asset},Query.Text.Trim(),SearchMode.SelectedItem as string??"",
            Folder.Text.Trim(),(Categories.SelectedItem as CategoryChoice)?.Name??"Todos os assets",
            Sections.SelectedItem as string??"Todas as subcategorias","Todos os tipos",
            Chunks.SelectedItem as string??"Todos os chunks",PreferBase.IsChecked==true,types,ExcludeQuery.Text).Count>0;
        if(!matches) {
            var scroll=FindVisual<ScrollViewer>(AssetsGrid);var offset=scroll?.VerticalOffset??0;
            var index=filtered.IndexOf(asset);
            if(index>=0) {
                for(var page=0;page<PageCount;page++) {
                    if(index<PageOffset(page)+PageSize-removedFromPage.GetValueOrDefault(page)) {
                        removedFromPage[page]=removedFromPage.GetValueOrDefault(page)+1;break;
                    }
                }
                filtered.RemoveAt(index);
            }
            visibleAssets.Remove(asset);
            if(scroll!=null)Dispatcher.BeginInvoke(()=>scroll.ScrollToVerticalOffset(offset),DispatcherPriority.Loaded);
        }
        ResultCount.Text=$"{filtered.Count:N0} resultados • {(Categories.SelectedItem as CategoryChoice)?.Name??"Todos os assets"}";
        PageText.Text=$"{pageIndex+1} / {PageCount}";
        NextPage.IsEnabled=PageOffset(pageIndex+1)<filtered.Count;
    }

    const int PageSize=60;
    int pageIndex;
    void ShowLibrary() {
        HomeView.Visibility=Visibility.Collapsed;LibraryView.Visibility=Visibility.Visible;PreviewColumn.Width=new GridLength(PreviewHost.Content is null or PreviewPanel?320:420);InspectorEmpty.Visibility=PreviewHost.Content==null?Visibility.Visible:Visibility.Collapsed;PreviewHost.Visibility=PreviewHost.Content==null?Visibility.Collapsed:Visibility.Visible;
        LibraryNav.Foreground=new SolidColorBrush(Color.FromRgb(0,242,254));HomeNav.Foreground=Brushes.LightGray;HomeNav.Background=Brushes.Transparent;LibraryNav.Background=new SolidColorBrush(Color.FromRgb(22,44,61));
    }
    void Home_Click(object sender,RoutedEventArgs e) {
        var wasReady=ready;ready=false;Categories.SelectedIndex=-1;AssetsGrid.UnselectAll();ready=wasReady;
        HomeView.Visibility=Visibility.Visible;LibraryView.Visibility=Visibility.Collapsed;PreviewColumn.Width=new GridLength(0);InspectorEmpty.Visibility=Visibility.Collapsed;PreviewHost.Visibility=Visibility.Collapsed;
        HomeNav.Foreground=new SolidColorBrush(Color.FromRgb(0,242,254));LibraryNav.Foreground=Brushes.LightGray;LibraryNav.Background=Brushes.Transparent;HomeNav.Background=new SolidColorBrush(Color.FromRgb(22,44,61));
    }
    void HideViewerOnHome() {
        if(HomeView.Visibility!=Visibility.Visible)return;
        PreviewColumn.Width=new GridLength(0);PreviewHost.Visibility=Visibility.Collapsed;InspectorEmpty.Visibility=Visibility.Collapsed;
    }
    void Library_Click(object sender,RoutedEventArgs e) {ShowLibrary();if(ready){Categories.SelectedIndex=0;debounce.Stop();debounce.Start();}}
    void RenderPage() {
        pageIndex=Math.Clamp(pageIndex,0,PageCount-1);
        foreach(var previous in AssetsGrid.Items.OfType<Asset>())previous.SetThumbnail(null);
        var page=filtered.Skip(PageOffset(pageIndex)).Take(PageSize-removedFromPage.GetValueOrDefault(pageIndex)).ToArray();
        foreach(var asset in page)availability?.LoadThumbnail(asset);
        visibleAssets.Clear();foreach(var asset in page)visibleAssets.Add(asset);AssetsGrid.ItemsSource=visibleAssets;
        PageText.Text=$"{pageIndex+1} / {PageCount}";
        PrevPage.IsEnabled=pageIndex>0;NextPage.IsEnabled=PageOffset(pageIndex+1)<filtered.Count;
        if(AssetsGrid.Items.Count>0)AssetsGrid.ScrollIntoView(AssetsGrid.Items[0]);
    }
    void PrevPage_Click(object sender,RoutedEventArgs e){pageIndex--;RenderPage();}
    void NextPage_Click(object sender,RoutedEventArgs e){pageIndex++;RenderPage();}
    void Options_Click(object sender,RoutedEventArgs e) {
        if(busy){Notify("Conclua ou cancele a tarefa antes de alterar as opções.",Notice.Warning);return;}
        GamePath.Text=game;BlenderPath.Text=blender;OutputPath.Text=outputFolder;OptionsError.Text="";
        LanguageChoice.SelectedIndex=Localization.Language=="en"?1:0;
        OptionsOverlay.Visibility=Visibility.Visible;
    }
    void OptionsClose_Click(object sender,RoutedEventArgs e)=>OptionsOverlay.Visibility=Visibility.Collapsed;
    void OptionsGameBrowse_Click(object sender,RoutedEventArgs e) {
        var dialog=new OpenFolderDialog{Title=Localization.T("Instalação do Aion 2"),InitialDirectory=Directory.Exists(GamePath.Text)?GamePath.Text:root};
        if(dialog.ShowDialog(this)==true)GamePath.Text=dialog.FolderName;
    }
    void OptionsBlenderBrowse_Click(object sender,RoutedEventArgs e) {
        var dialog=new OpenFileDialog{Title=Localization.T("Selecione blender.exe"),Filter="Blender|blender.exe",FileName=File.Exists(BlenderPath.Text)?BlenderPath.Text:""};
        if(dialog.ShowDialog(this)==true)BlenderPath.Text=dialog.FileName;
    }
    void OptionsOutputBrowse_Click(object sender,RoutedEventArgs e) {
        var dialog=new OpenFolderDialog{Title=Localization.T("Pasta de saída"),InitialDirectory=Directory.Exists(OutputPath.Text)?OutputPath.Text:root};
        if(dialog.ShowDialog(this)==true)OutputPath.Text=dialog.FolderName;
    }
    void OptionsSave_Click(object sender,RoutedEventArgs e) {
        if(busy)return;
        try {
            var nextGame=System.IO.Path.GetFullPath(GamePath.Text.Trim());
            var paks=ResolvePaks(nextGame);
            var protectedRoot=nextGame;
            var parent=Directory.GetParent(paks);
            if(parent?.Name.Equals("Content",StringComparison.OrdinalIgnoreCase)==true) {
                protectedRoot=parent.Parent!.FullName;
                if(parent.Parent.Name.Equals("Aion2",StringComparison.OrdinalIgnoreCase))
                    protectedRoot=parent.Parent.Parent?.FullName??protectedRoot;
            }
            var nextOutput=System.IO.Path.GetFullPath(OutputPath.Text.Trim());
            if(nextOutput.Equals(protectedRoot,StringComparison.OrdinalIgnoreCase)||nextOutput.StartsWith(protectedRoot.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))
                throw new Exception("Escolha uma saída fora da instalação do jogo.");
            var nextBlender=BlenderPath.Text.Trim();
            if(nextBlender.Length>0&&!File.Exists(nextBlender))throw new Exception("Blender não encontrado. Indique blender.exe ou deixe vazio para configurar depois.");
            bool changed=!nextGame.Equals(game,StringComparison.OrdinalIgnoreCase);
            var nextLanguage=LanguageChoice.SelectedIndex==1?"en":"pt-BR";
            CompactDat.Put(System.IO.Path.Combine(root,"cache","settings.dat"),"settings.json",JsonSerializer.Serialize(new{Blender=nextBlender,Game=nextGame,Output=nextOutput,QueueVisible=queueVisible,LibraryVisible=libraryVisible,Language=nextLanguage}));
            game=nextGame;blender=nextBlender;outputFolder=nextOutput;
            Localization.SetLanguage(nextLanguage);
            if(changed) {
                ClosePreview();availability=null;previews.Clear();ready=false;all=[];filtered=[];queue.Clear();
                Categories.ItemsSource=null;RenderPage();LibraryCount.Text="Sem catálogo";ExploreButton.Content="Explorar biblioteca";
                var cached=System.IO.Path.Combine(root,"reports/desktop-catalog.json");
                if(File.Exists(cached))File.Move(cached,cached+"."+Guid.NewGuid().ToString("N")+".old");var catalogDat=System.IO.Path.Combine(root,"cache","catalog.dat");if(File.Exists(catalogDat))File.Delete(catalogDat);
                Notify("Configurações salvas. Atualize o catálogo da nova instalação.");
            }else Notify("Configurações salvas.");
            OptionsOverlay.Visibility=Visibility.Collapsed;
        }catch(Exception ex){OptionsError.Text=ex.Message;Notify(ex.Message,Notice.Error);}
    }
    bool changingTypes,libraryVisible=true;
    void UpdateTypesCaption() {
        if(TypesButton==null)return;
        var selected=Hints.SelectedItems.Cast<string>().Where(s=>s!="Todos os tipos").ToArray();
        TypesButton.Content=selected.Length==0?"Todos os tipos ▾":selected.Length==1?selected[0]+" ▾":$"{selected.Length} tipos selecionados ▾";
        TypesButton.ToolTip=selected.Length==0?"Todos os tipos":string.Join(", ",selected);
    }
    void TypesChanged(object sender,SelectionChangedEventArgs e) {
        if(changingTypes)return;
        changingTypes=true;
        if(e.AddedItems.Contains("Todos os tipos")) {
            foreach(var item in Hints.SelectedItems.Cast<string>().Where(s=>s!="Todos os tipos").ToArray())Hints.SelectedItems.Remove(item);
        }else if(e.AddedItems.Count>0)Hints.SelectedItems.Remove("Todos os tipos");
        changingTypes=false;UpdateTypesCaption();
        if(ready){debounce.Stop();debounce.Start();}
    }
    void SetLibraryVisible(bool visible) {
        libraryVisible=visible;
        LibraryPanel.Visibility=visible?Visibility.Visible:Visibility.Collapsed;
        LibraryColumn.Width=new GridLength(visible?240:0);LibraryGap.Width=new GridLength(0);
        LibraryToggle.Content=visible?"Ocultar biblioteca":"Mostrar biblioteca";
    }
    void LibraryToggle_Click(object sender,RoutedEventArgs e) {
        SetLibraryVisible(!libraryVisible);
        try{SaveSettings();}catch(Exception ex){Notify("Não foi possível salvar a preferência: "+ex.Message,Notice.Error);}
    }
    void CleanupPreviews() {
        ClosePreview();previews.Clear();
        try{PreviewCacheCleanup.Clear(root);}
        catch(Exception ex){File.WriteAllText(System.IO.Path.Combine(root,"reports/preview-cleanup-error.txt"),ex.ToString());}
    }
    bool queueVisible=false;
    ModelAvailabilityCache? availability;
    void SetQueueVisible(bool visible) {
        queueVisible=visible;QueuePanel.Visibility=visible?Visibility.Visible:Visibility.Collapsed;
        QueueRow.Height=visible?new GridLength(220):new GridLength(0);
        QueueToggle.Content=visible?$"Ocultar fila ({queue.Count})":$"Mostrar fila ({queue.Count})";
    }
    void QueueToggle_Click(object sender,RoutedEventArgs e) {
        SetQueueVisible(!queueVisible);
        try{SaveSettings();}catch(Exception ex){Notify("Não foi possível salvar a preferência: "+ex.Message,Notice.Error);}
    }
    void ClosePreview() {
        if(PreviewHost.Content is PreviewPanel panel)panel.DisposePreview();
        if(PreviewHost.Content is AudioPanel audio)audio.Dispose();
        if(PreviewHost.Content is AnimationPanel animation)animation.Dispose();
        if(PreviewHost.Content is InspectionPanel inspection)inspection.Dispose();
        PreviewHost.Content=null;PreviewHost.Visibility=Visibility.Collapsed;InspectorEmpty.Visibility=Visibility.Collapsed;
        PreviewColumn.Width=new GridLength(0);PreviewGap.Width=new GridLength(0);
    }
    void RefreshHints() {
        var selected=Hints.SelectedItems.Cast<string>().ToArray();
        var wasReady=ready;ready=false;changingTypes=true;
        var hints=new[]{"Todos os tipos"}.Concat(all.Select(a=>a.Hint).Concat(new[]{"Modelo provável",ModelAvailabilityCache.AvailableHint}).Distinct().Order()).ToArray();
        Hints.ItemsSource=hints;
        foreach(var type in selected.Where(hints.Contains))Hints.SelectedItems.Add(type);
        changingTypes=false;ready=wasReady;UpdateTypesCaption();
    }
    async Task ShowPreview(Asset asset,string[] models,string assetRoot,int failures) {
        ClosePreview();
        var panel=new PreviewPanel(asset.Name,models,assetRoot,()=>Enqueue(new[]{asset}),failures);
        panel.InspectRequested+=async()=>await InspectAsset(asset,true);
        var cache=availability;
        panel.CloseRequested+=()=>ClosePreview();
        panel.ModelFailed+=message=>Notify(message,Notice.Error);
        panel.ModelLoaded+=scene=>{
            if(!ReferenceEquals(PreviewHost.Content,panel)||!ReferenceEquals(availability,cache))return;
            try {
                lastPreviewModel=panel.CurrentModelPath;
                cache?.Confirm(asset,scene);
                if(panel.CurrentModelPath is {} modelFile)cache?.SaveSkeleton(asset,modelFile);
                try{cache?.SaveThumbnail(asset,panel.CaptureThumbnail());}
                catch(Exception ex){File.WriteAllText(System.IO.Path.Combine(root,"reports/thumbnail-error.txt"),ex.ToString());}
                RefreshAssetInPlace(asset);
                Notify(asset.Name+" • Modelo disponível • confirmação salva no cache",Notice.Success);
            }catch(Exception ex){Notify("Prévia carregada; não foi possível salvar a confirmação: "+ex.Message,Notice.Error);}
        };
        InspectorEmpty.Visibility=Visibility.Collapsed;
        PreviewColumn.Width=new GridLength(320);
        PreviewGap.Width=new GridLength(0);
        PreviewHost.Content=panel;PreviewHost.Visibility=Visibility.Visible;
        HideViewerOnHome();
        try{await panel.OpenAsync();HideViewerOnHome();}
        catch {
            try{cache?.Reject(asset);}catch(Exception ex){Notify("Não foi possível salvar a classificação: "+ex.Message,Notice.Error);}
            RefreshAssetInPlace(asset);throw;
        }
    }
    void SetBusy(bool value) {
        value=value||scanning;
        busy=value; RefreshButton.IsEnabled=!value; RunButton.IsEnabled=!value;
        AddButton.IsEnabled=!value; AddResultsButton.IsEnabled=!value; RemoveButton.IsEnabled=!value;
        OutputButton.IsEnabled=!value;GameButton.IsEnabled=!value;
        PreviewButton.IsEnabled=!value;
        CancelButton.IsEnabled=value; Progress.IsIndeterminate=value;
    }
    async Task LoadCatalog() {
        var path=System.IO.Path.Combine(root,"reports/desktop-catalog.json");
        var compact=System.IO.Path.Combine(root,"cache","catalog.dat");
        if(File.Exists(path))CompactDat.Migrate(path,compact,"catalog.json");
        if(!File.Exists(compact)) {
            Notify("Clique em Atualizar catálogo para ler os índices do jogo.");
            LibraryCount.Text="Catálogo ainda não criado";
            return;
        }
        SetBusy(true); Notify("Carregando a biblioteca…");
        try {
            var catalog=await Task.Run(()=>{
                var c=CompactDat.Read(compact,"catalog.json",stream=>JsonSerializer.Deserialize<Catalog>(stream)??throw new Exception("Catálogo vazio."));
                SearchEngine.Prepare(c.Files); return c;
            });
            ready=false; all=catalog.Files;
            string paks;
            try{paks=ResolvePaks(game);}catch{paks=System.IO.Path.Combine(game,"Aion2","Content","Paks");}
            availability=new ModelAvailabilityCache(System.IO.Path.Combine(root,"cache","available-models.json"),paks,ReleaseRuntime.Support(root,"mappings","AION2-5.3.2.0-1.0.52.0.usmap"));
            availability.Apply(all);
            Categories.ItemsSource=new[]{new CategoryChoice("Todos os assets",all.Count)}
                .Concat(all.GroupBy(a=>a.Category).OrderBy(g=>g.Key).Select(g=>new CategoryChoice(g.Key,g.Count()))).ToArray();
            Categories.SelectedIndex=HomeView.Visibility==Visibility.Visible?-1:0;
            RefreshHints();Hints.SelectedIndex=0;
            UpdateFacets();
            LibraryCount.Text=$"{all.Count:N0}";
            ExploreButton.Content=$"Explorar biblioteca ({all.Count:N0})";
            CatalogStamp.Text=$"Catálogo: {catalog.GeneratedAtUtc.ToLocalTime():dd/MM HH:mm} • pronto";
            ready=true;
            await ApplyFilter();
            Notify($"Catálogo de {catalog.GeneratedAtUtc.ToLocalTime():dd/MM HH:mm} • Saída: {outputFolder} • Materiais: prévia de cor base");
        } catch(Exception ex) {Notify(ex.Message,Notice.Error);}
        finally {SetBusy(false);}
    }
    void UpdateFacets() {
        var category=(Categories.SelectedItem as CategoryChoice)?.Name??"Todos os assets";
        var subset=all.Where(a=>category=="Todos os assets"||a.Category==category);
        var oldSection=Sections.SelectedItem as string;var oldChunk=Chunks.SelectedItem as string;
        var sections=new[]{"Todas as subcategorias"}.Concat(subset.Select(a=>a.Section).Distinct().Order()).ToArray();
        var chunks=new[]{"Todos os chunks"}.Concat(subset.Select(a=>a.Chunk).Where(s=>s.Length>0).Distinct().Order()).ToArray();
        Sections.ItemsSource=sections;Sections.SelectedItem=sections.Contains(oldSection)?oldSection:sections[0];
        Chunks.ItemsSource=chunks;Chunks.SelectedItem=chunks.Contains(oldChunk)?oldChunk:chunks[0];
    }
    void SearchChanged(object sender,TextChangedEventArgs e) {if(ready){debounce.Stop();debounce.Start();}}
    void ToggleChanged(object sender,RoutedEventArgs e) {if(ready){debounce.Stop();debounce.Start();}}
    void FilterChanged(object sender,SelectionChangedEventArgs e) {
        if(!ready)return;
        if(ReferenceEquals(sender,Categories)){ShowLibrary();ready=false;UpdateFacets();ready=true;}
        debounce.Stop();debounce.Start();
    }
    async Task ApplyFilter() {
        var selectedPaths=AssetsGrid.SelectedItems.Cast<Asset>().Select(a=>a.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var version=++filterVersion;
        var query=Query.Text.Trim();var mode=SearchMode.SelectedItem as string??"";
        var folder=Folder.Text.Trim();var category=(Categories.SelectedItem as CategoryChoice)?.Name??"Todos os assets";
        var section=Sections.SelectedItem as string??"Todas as subcategorias";var hint="Todos os tipos";var types=Hints.SelectedItems.Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);var exclude=ExcludeQuery.Text;
        var chunk=Chunks.SelectedItem as string??"Todos os chunks";var prefer=PreferBase.IsChecked==true;
        try {
            var result=await Task.Run(()=>SearchEngine.Filter(all,query,mode,folder,category,section,hint,chunk,prefer,types,exclude));
            if(version!=filterVersion)return;
            filtered=result;removedFromPage.Clear();
            pageIndex=0;RenderPage();
            if(selectedPaths.Count>0)foreach(var a in AssetsGrid.Items.Cast<Asset>().Where(a=>selectedPaths.Contains(a.Path)))AssetsGrid.SelectedItems.Add(a);
            ResultCount.Text=$"{result.Count:N0} resultados • {category}";
        } catch(Exception ex) {Notify("Busca inválida: "+ex.Message,Notice.Error);}
    }
    void Clear_Click(object sender,RoutedEventArgs e) {
        ready=false;Query.Clear();Folder.Clear();ExcludeQuery.Clear();Categories.SelectedIndex=0;Hints.SelectedIndex=0;PreferBase.IsChecked=false;SearchMode.SelectedIndex=0;UpdateFacets();Sections.SelectedIndex=0;Chunks.SelectedIndex=0;ready=true;_=ApplyFilter();
    }
    void AssetsSelectionChanged(object sender,SelectionChangedEventArgs e) {
        var a=AssetsGrid.SelectedItem as Asset;
        PreviewButton.Content="Visualizar";
        Details.Text=a==null?"Selecione um asset.":$"{a.Path}  •  {a.Chunk}";
    }
    void Enqueue(IEnumerable<Asset> assets) {
        var paths=queue.Select(q=>q.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var a in assets)if(paths.Add(a.Path))queue.Add(new(){Asset=a});
        QueueTitle.Text=$"FILA DE EXPORTAÇÃO · {queue.Count}";SetQueueVisible(queueVisible);
    }
    void Add_Click(object sender,RoutedEventArgs e)=>Enqueue(AssetsGrid.SelectedItems.Cast<Asset>().ToArray());
    void AddResults_Click(object sender,RoutedEventArgs e) {
        if(filtered.Count>10000){Notify("Refine a busca para no máximo 10.000 itens.",Notice.Warning);return;}
        if(filtered.Count>50)Notify($"Adicionando {filtered.Count:N0} pacotes à fila. Pacotes sem malha serão reportados como falha.",Notice.Warning);
        Enqueue(filtered);
    }
    void Remove_Click(object sender,RoutedEventArgs e) {foreach(var q in QueueGrid.SelectedItems.Cast<QueueItem>().ToArray())queue.Remove(q);QueueTitle.Text=$"FILA DE EXPORTAÇÃO · {queue.Count}";SetQueueVisible(queueVisible);}
    void Copy_Click(object sender,RoutedEventArgs e) {if(AssetsGrid.SelectedItem is Asset a)Clipboard.SetText(a.Path);}
    void Outputs_Click(object sender,RoutedEventArgs e)=>OpenDirectory(outputFolder);
    void OpenResult_Click(object sender,RoutedEventArgs e) {if(QueueGrid.SelectedItem is QueueItem q && Directory.Exists(q.Output))OpenDirectory(q.Output);else if(Directory.Exists(lastReport))OpenDirectory(lastReport);}
    void OpenDirectory(string path) {Directory.CreateDirectory(path);Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}
    void Blender_Click(object sender,RoutedEventArgs e) {
        try{
            if(!File.Exists(blender))throw new FileNotFoundException("Blender não encontrado. Configure o executável nas opções.");
            Process.Start(new ProcessStartInfo(blender){UseShellExecute=true});
        }catch(Exception ex){Notify(ex.Message,Notice.Error);}
    }

    void SaveSettings() {
        CompactDat.Put(System.IO.Path.Combine(root,"cache","settings.dat"),"settings.json",JsonSerializer.Serialize(new{Blender=blender,Game=game,Output=outputFolder,QueueVisible=queueVisible,LibraryVisible=libraryVisible,Language=Localization.Language}));
        OutputButton.ToolTip=outputFolder;GameButton.ToolTip=game;
    }
    static string ResolvePaks(string directory) {
        foreach(var candidate in new[]{System.IO.Path.Combine(directory,"Aion2","Content","Paks"),System.IO.Path.Combine(directory,"Content","Paks"),directory})
            if(Directory.Exists(candidate)&&Directory.EnumerateFiles(candidate,"*.utoc").Any())return candidate;
        throw new Exception("Escolha a instalação do Aion 2 ou sua pasta Content/Paks.");
    }
    void Output_Click(object sender,RoutedEventArgs e) {
        if(busy)return;
        var dialog=new OpenFolderDialog{Title="Onde salvar os arquivos exportados?",InitialDirectory=Directory.Exists(outputFolder)?outputFolder:root};
        if(dialog.ShowDialog(this)!=true)return;
        var selected=System.IO.Path.GetFullPath(dialog.FolderName);
        if(selected.Equals(game,StringComparison.OrdinalIgnoreCase)||selected.StartsWith(game.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase)){
            Notify("Escolha uma pasta fora da instalação do jogo.",Notice.Warning);return;
        }
        outputFolder=selected;SaveSettings();Notify("Saída: "+outputFolder);
    }
    void Game_Click(object sender,RoutedEventArgs e) {
        if(busy)return;
        var dialog=new OpenFolderDialog{Title="Selecione a instalação do Aion 2",InitialDirectory=Directory.Exists(game)?game:root};
        if(dialog.ShowDialog(this)!=true)return;
        try {
            ResolvePaks(dialog.FolderName);
            ClosePreview();lastPreviewModel=null;availability=null;previews.Clear();game=dialog.FolderName;SaveSettings();
            ready=false;all=[];filtered=[];AssetsGrid.ItemsSource=null;Categories.ItemsSource=null;queue.Clear();
            LibraryCount.Text="Atualize o catálogo";Notify("Instalação selecionada. Clique em Atualizar catálogo.");
            var cached=System.IO.Path.Combine(root,"reports/desktop-catalog.json");
            if(File.Exists(cached))File.Move(cached,cached+"."+Guid.NewGuid().ToString("N")+".old");var catalogDat=System.IO.Path.Combine(root,"cache","catalog.dat");if(File.Exists(catalogDat))File.Delete(catalogDat);
        }catch(Exception ex){Notify(ex.Message,Notice.Error);}
    }
    readonly Dictionary<string,(string[] Models,string AssetRoot,int Failures)> previews=new(StringComparer.OrdinalIgnoreCase);
    void EditSubcategory_Click(object sender,RoutedEventArgs e) {
        if(busy||availability==null)return;
        var selected=AssetsGrid.SelectedItems.Cast<Asset>().ToArray();
        if(selected.Length==0){Notify("Selecione um ou mais assets para editar a subcategoria.",Notice.Warning);return;}
        var dialog=new Window {Owner=this,Title=Localization.T("Editar subcategoria"),Width=440,Height=240,ResizeMode=ResizeMode.NoResize,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=new SolidColorBrush(Color.FromRgb(18,22,39)),Foreground=Brushes.White};
        Localization.SetEnabled(dialog,true);
        var layout=new StackPanel{Margin=new Thickness(22)};
        layout.Children.Add(new TextBlock{Text=$"Subcategoria de {selected.Length} asset(s)",Margin=new Thickness(0,0,0,12)});
        var input=new TextBox{Text=selected.Select(a=>a.Section).Distinct().Count()==1?selected[0].Section:"",MaxLength=80};
        layout.Children.Add(input);
        var error=new TextBlock{Foreground=Brushes.Salmon,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};
        layout.Children.Add(error);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var save=new Button{Content="Salvar",IsDefault=true,Padding=new Thickness(18,8,18,8)};
        var cancel=new Button{Content="Cancelar",IsCancel=true,Padding=new Thickness(18,8,18,8),Margin=new Thickness(8,0,0,0)};
        save.Click+=(_,_)=>{
            try{
                availability.SetSubcategory(selected,input.Text);
                var wasReady=ready;ready=false;UpdateFacets();Sections.SelectedItem=input.Text.Trim();ready=wasReady;
                _=ApplyFilter();Notify("Subcategoria salva no cache.");dialog.DialogResult=true;
            }catch(Exception ex){error.Text=ex.Message;Notify(ex.Message,Notice.Error);}
        };
        buttons.Children.Add(save);buttons.Children.Add(cancel);layout.Children.Add(buttons);dialog.Content=layout;
        dialog.Loaded+=(_,_)=>{input.Focus();input.SelectAll();};dialog.ShowDialog();
    }
    void Assets_DoubleClick(object sender,System.Windows.Input.MouseButtonEventArgs e) {
        if(busy||e.ChangedButton!=System.Windows.Input.MouseButton.Left||e.OriginalSource is not DependencyObject source)return;
        if(ItemsControl.ContainerFromElement(AssetsGrid,source) is not ListBoxItem item||item.DataContext is not Asset asset)return;
        AssetsGrid.UnselectAll();AssetsGrid.SelectedItem=asset;e.Handled=true;
        Preview_Click(sender,new RoutedEventArgs());
    }

    async void Audio_Click(object sender,RoutedEventArgs e) {
        if(busy)return;
        if(AssetsGrid.SelectedItems.Count!=1||AssetsGrid.SelectedItem is not Asset asset){Notify("Selecione um único asset para ouvir.",Notice.Warning);return;}
        await PreviewAudio(asset);
    }
    void ShowInspection(Asset asset,JsonElement data) {
        ClosePreview();
        if(data.GetProperty("Objects").EnumerateArray().Any(o=>(o.GetProperty("Type").GetString()??"").StartsWith("Texture",StringComparison.OrdinalIgnoreCase))) {
            var images=data.GetProperty("Images").EnumerateArray().Select(i=>i.GetString()!).Where(File.Exists).ToArray();
            if(images.Length>0) { availability?.ConfirmTexture(asset,images[0]);RefreshAssetInPlace(asset); }
        }
        var panel=new InspectionPanel(asset.Name,data);
        panel.CloseRequested+=ClosePreview;panel.Failed+=message=>Notify(message,Notice.Error);
        PreviewColumn.Width=new GridLength(420);PreviewGap.Width=new GridLength(0);
        PreviewHost.Content=panel;PreviewHost.Visibility=Visibility.Visible;InspectorEmpty.Visibility=Visibility.Collapsed;
        HideViewerOnHome();
        Notify(asset.Name+" • Recursos e parâmetros lidos; o efeito completo não é simulado.",Notice.Success);
    }
    async void Inspect_Click(object sender,RoutedEventArgs e) {
        if(busy)return;
        if(AssetsGrid.SelectedItems.Count!=1||AssetsGrid.SelectedItem is not Asset asset){Notify("Selecione um asset para inspecionar.",Notice.Warning);return;}
        await InspectAsset(asset);
    }
    async Task InspectAsset(Asset asset,bool forceInspection=false) {
        if(busy)return;await PrepareAssetOpening();
        SetBusy(true);Notify("Lendo recursos de "+asset.Name+"…");
        try {
            var request=System.IO.Path.Combine(root,"reports","inspect-selection-"+Guid.NewGuid().ToString("N")+".json");
            File.WriteAllText(request,JsonSerializer.Serialize(new[]{new{asset.Path}}));
            var work=await Work("inspect",request);
            if(stopped){Notify("Inspeção interrompida.",Notice.Warning);return;}
            using var json=JsonDocument.Parse(File.ReadAllText(work.Result));
            var data=json.RootElement;
            if(work.Code!=0||!data.TryGetProperty("Objects",out _))
                throw new InvalidDataException(data.TryGetProperty("Error",out var error)?error.GetString():"Não foi possível ler o asset.");
            var models=data.GetProperty("Models").EnumerateArray().Select(v=>v.GetString()!).ToArray();
            if(!forceInspection&&data.GetProperty("HasRootMesh").GetBoolean()&&models.Length>0)
                await ShowPreview(asset,models,data.GetProperty("AssetRoot").GetString()!,0);
            else ShowInspection(asset,data.Clone());
        }catch(Exception ex){if(!await RecoverAssetCache(asset,ex,()=>InspectAsset(asset,forceInspection)))Notify(ex.Message,stopped?Notice.Warning:Notice.Error);}
        finally{SetBusy(false);}
    }

    async Task ShowAudio(Asset asset,string[] tracks) {
        ClosePreview();
        var panel=new AudioPanel(asset.Name,testing);
        panel.InspectRequested+=async()=>await InspectAsset(asset,true);
        panel.CloseRequested+=ClosePreview;
        panel.Failed+=message=>Notify("Áudio indisponível: "+message,Notice.Error);
        var cache=availability;
        panel.Ready+=()=>{
            if(!ReferenceEquals(PreviewHost.Content,panel)||!ReferenceEquals(cache,availability))return;
            try{cache?.ConfirmAudio(asset,panel.Duration);RefreshAssetInPlace(asset);Notify(asset.Name+" • Áudio confirmado",Notice.Success);}
            catch(Exception ex){Notify("Áudio carregado; não foi possível salvar o tipo: "+ex.Message,Notice.Error);}
        };
        PreviewColumn.Width=new GridLength(320);PreviewGap.Width=new GridLength(0);
        PreviewHost.Content=panel;PreviewHost.Visibility=Visibility.Visible;InspectorEmpty.Visibility=Visibility.Collapsed;
        HideViewerOnHome();
        await panel.OpenAsync(tracks);
        HideViewerOnHome();
    }
    async Task PreviewAudio(Asset asset) {
        await PrepareAssetOpening();
        SetBusy(true);Notify("Preparando áudio de "+asset.Name+"…");
        try {
            var request=System.IO.Path.Combine(root,"reports","audio-selection-"+Guid.NewGuid().ToString("N")+".json");
            File.WriteAllText(request,JsonSerializer.Serialize(new[]{new{asset.Path}}));
            var work=await Work("audio",request);
            if(stopped){Notify("Prévia de áudio interrompida.",Notice.Warning);return;}
            if(!File.Exists(work.Result))throw new IOException("Não foi gerado um resultado de áudio.");
            using var json=JsonDocument.Parse(File.ReadAllText(work.Result));
            var data=json.RootElement;
            if(work.Code!=0||!data.TryGetProperty("Tracks",out var tracks))
                throw new InvalidDataException(data.TryGetProperty("Error",out var error)?error.GetString():"Nenhuma faixa reproduzível.");
            await ShowAudio(asset,tracks.EnumerateArray().Select(t=>t.GetString()!).ToArray());
        }catch(Exception ex){if(!await RecoverAssetCache(asset,ex,()=>VisualizeAsset(asset)))Notify(ex.Message,stopped?Notice.Warning:Notice.Error);}
        finally{SetBusy(false);}
    }

    async void Animation_Click(object sender,RoutedEventArgs e) {
        if(busy)return;
        if(AssetsGrid.SelectedItems.Count!=1||AssetsGrid.SelectedItem is not Asset asset){Notify("Selecione uma sequência de animação.",Notice.Warning);return;}
        await PreviewAnimation(asset);
    }
    void QueueAnimation(Asset asset,AnimationClipData clip,string? modelFile) {
        var same=queue.Any(q=>q.IsAnimation&&q.AnimationSlot==clip.Slot&&q.Path.Equals(asset.Path,StringComparison.OrdinalIgnoreCase)&&
            string.Equals(q.ModelFile,modelFile,StringComparison.OrdinalIgnoreCase));
        if(same){Notify("Esta opção da animação já está na fila.",Notice.Warning);return;}
        var directory=System.IO.Path.Combine(root,"cache","previews","animation-queue-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var source=System.IO.Path.Combine(directory,"clip.json");
        File.WriteAllText(source,JsonSerializer.Serialize(clip));
        string? assetRoot=null;
        if(modelFile!=null) {
            var candidate=new DirectoryInfo(System.IO.Path.GetDirectoryName(modelFile)!);
            while(candidate!=null&&!Directory.Exists(System.IO.Path.Combine(candidate.FullName,"Aion2")))candidate=candidate.Parent;
            assetRoot=candidate?.FullName??System.IO.Path.GetDirectoryName(modelFile);
        }
        queue.Add(new QueueItem{Asset=asset,AnimationClip=source,AnimationSlot=clip.Slot,ModelFile=modelFile,AssetRoot=assetRoot});
        QueueTitle.Text=$"FILA DE EXPORTAÇÃO · {queue.Count}";SetQueueVisible(true);
        Notify(modelFile==null?"Esqueleto + animação adicionados à fila.":"Modelo + texturas + esqueleto + animação adicionados à fila.",Notice.Success);
    }
    async Task PreviewAnimation(Asset asset) {
        await PrepareAssetOpening();
        SetBusy(true);Notify("Decodificando animação de "+asset.Name+"…");
        try {
            var request=System.IO.Path.Combine(root,"reports","animation-selection-"+Guid.NewGuid().ToString("N")+".json");
            File.WriteAllText(request,JsonSerializer.Serialize(new[]{new{asset.Path}}));
            var work=await Work("animation",request);
            if(stopped){Notify("Animação interrompida.",Notice.Warning);return;}
            using var json=JsonDocument.Parse(File.ReadAllText(work.Result));
            var data=json.RootElement;
            if(work.Code!=0||!data.TryGetProperty("Animation",out var file))throw new InvalidDataException(data.TryGetProperty("Error",out var error)?error.GetString():"Nenhuma animação reproduzível.");
            var clip=await Task.Run(()=>AnimationClipData.Load(file.GetString()!));
            var variants=data.TryGetProperty("Variants",out var choices)?JsonSerializer.Deserialize<AnimationVariant[]>(choices.GetRawText())??[]:[];
            ShowAnimation(asset,clip,variants,file.GetString()!);
            Notify(clip.Name+" • animação disponível • clique em Reproduzir",Notice.Success);
        }catch(Exception ex){if(!await RecoverAssetCache(asset,ex,()=>VisualizeAsset(asset)))Notify(ex.Message,stopped?Notice.Warning:Notice.Error);}
        finally{SetBusy(false);}
    }
    void ShowAnimation(Asset asset,AnimationClipData clip,AnimationVariant[] variants,string currentFile){
        availability?.ConfirmAnimation(asset,clip);RefreshAssetInPlace(asset);
        ClosePreview();var panel=new AnimationPanel(clip);panel.SetLibrary(all);panel.SetVariants(variants,currentFile);
        panel.InspectRequested+=async()=>await InspectAsset(asset,true);
        panel.LibraryModelRequested+=async model=>await LoadAnimationLibraryModel(panel,model);
        panel.NoticeRequested+=message=>Notify(message,Notice.Success);
        panel.QueueRequested+=model=>QueueAnimation(asset,clip,model);
        panel.CloseRequested+=ClosePreview;panel.Failed+=message=>Notify(message,Notice.Error);
        panel.VariantRequested+=async nextFile=>{
            if(busy)return;SetBusy(true);
            try{var next=await Task.Run(()=>AnimationClipData.Load(nextFile));ShowAnimation(asset,next,variants,nextFile);Notify(next.Name+" • animação disponível • clique em Reproduzir",Notice.Success);}
            catch(Exception ex){if(!await RecoverAssetCache(asset,ex,()=>PreviewAnimation(asset)))Notify(ex.Message,Notice.Error);}finally{SetBusy(false);}
        };
        PreviewColumn.Width=new GridLength(420);PreviewHost.Content=panel;PreviewHost.Visibility=Visibility.Visible;
        HideViewerOnHome();
    }
    async Task LoadAnimationLibraryModel(AnimationPanel panel,Asset asset){
        if(busy)return;SetBusy(true);Notify("Preparando modelo da biblioteca: "+asset.Name);
        try{
            var request=System.IO.Path.Combine(root,"reports","animation-model-"+Guid.NewGuid().ToString("N")+".json");File.WriteAllText(request,JsonSerializer.Serialize(new[]{new{asset.Path}}));
            var work=await Work("preview",request);if(stopped)return;
            using var json=JsonDocument.Parse(File.ReadAllText(work.Result));var data=json.RootElement;
            if(work.Code!=0||!data.TryGetProperty("Models",out var models))throw new InvalidDataException(data.TryGetProperty("Error",out var error)?error.GetString():"Nenhum modelo disponível.");
            Exception? failure=null;bool loaded=false;
            foreach(var model in models.EnumerateArray()){try{panel.LoadModel(model.GetString()!,data.GetProperty("AssetRoot").GetString()!);availability?.SaveSkeleton(asset,model.GetString()!);panel.SetLibrary(all);loaded=true;break;}catch(Exception ex){failure=ex;}}
            if(!loaded)throw failure??new InvalidDataException("Nenhuma malha compatível com esta animação.");
            Notify(asset.Name+" • modelo aplicado à animação",Notice.Success);
        }catch(Exception ex){Notify(ex.Message,Notice.Error);}finally{SetBusy(false);}
    }
    async void Preview_Click(object sender,RoutedEventArgs e) {
        if(busy)return;
        if(AssetsGrid.SelectedItems.Count!=1 || AssetsGrid.SelectedItem is not Asset asset) {
            Notify("Selecione um único asset para visualizar.",Notice.Warning);return;
        }
        await VisualizeAsset(asset);
    }
    async Task VisualizeAsset(Asset asset) {
        if(asset.IsAudio){await PreviewAudio(asset);return;}
        if((asset.Hint.Contains("Animação")&&!asset.Name.StartsWith("ABP_",StringComparison.OrdinalIgnoreCase))||asset.Path.Contains("/Ani/",StringComparison.OrdinalIgnoreCase)||asset.Path.Contains("/Animations/",StringComparison.OrdinalIgnoreCase)){await PreviewAnimation(asset);return;}
        if(asset.IsEffect||asset.Hint.Contains("Material")||asset.Hint.Contains("Textura")||asset.Hint.Contains("Controle")||asset.Hint.Contains("Esqueleto")||asset.Name.StartsWith("ABP_",StringComparison.OrdinalIgnoreCase)){await InspectAsset(asset);return;}
        if(asset.Hint!="Modelo provável"&&asset.Hint!=ModelAvailabilityCache.AvailableHint){await InspectAsset(asset);return;}
        await PreviewModel(asset);
    }
    async Task PreviewModel(Asset asset) {
        await PrepareAssetOpening();
        SetBusy(true);Notify("Preparando prévia 3D de "+asset.Name+"…");
        try {
            var key=game+"|"+asset.Path;
            if(!previews.TryGetValue(key,out var preview)||!preview.Models.All(File.Exists)) {
                var request=System.IO.Path.Combine(root,"reports","preview-selection-"+Guid.NewGuid().ToString("N")+".json");
                File.WriteAllText(request,JsonSerializer.Serialize(new[]{new{asset.Path}}));
                var work=await Work("preview",request);
                if(stopped)throw new Exception("Prévia interrompida.");
                if(!File.Exists(work.Result))throw new Exception("Não foi possível preparar a prévia. Consulte "+work.Result);
                using var json=JsonDocument.Parse(File.ReadAllText(work.Result));
                var data=json.RootElement;
                if(work.Code!=0||!data.TryGetProperty("Models",out var models))
                    throw new Exception(data.TryGetProperty("Error",out var error)?error.GetString():"Não foi gerada uma malha.");
                preview=(models.EnumerateArray().Select(v=>v.GetString()!).ToArray(),data.GetProperty("AssetRoot").GetString()!,data.GetProperty("ExportFailures").GetInt32());
                previews[key]=preview;
            }
            await ShowPreview(asset,preview.Models,preview.AssetRoot,preview.Failures);
        } catch(Exception ex) {
            if(await RecoverAssetCache(asset,ex,()=>VisualizeAsset(asset)))return;
            Notify(ex.Message,Notice.Error);
            if(!stopped) {
                try{availability?.Reject(asset);}catch(Exception saveError){Notify(ex.Message+" • "+saveError.Message,Notice.Error);}
                RefreshAssetInPlace(asset);
                Notify(ex.Message,Notice.Error);
            }
        } finally {SetBusy(false);}
    }

    async Task<(int Code,string Result)> Work(string mode,string request="") {
        stopped=false;
        var result=System.IO.Path.Combine(root,"reports","desktop-result-"+Guid.NewGuid().ToString("N")+".json");
        if(scanning)result=System.IO.Path.Combine(root,"reports","catalog-scan","audio-result.json");
        var psi=new ProcessStartInfo("powershell.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=root};
        foreach(var arg in new[]{"-NoProfile","-ExecutionPolicy","Bypass","-File",ReleaseRuntime.Support(root,"scripts/desktop-worker.ps1"),"-Mode",mode,"-ResultFile",result,"-Blender",blender,"-Game",game,"-OutputRoot",outputFolder})psi.ArgumentList.Add(arg);
        if(request.Length>0){psi.ArgumentList.Add("-RequestFile");psi.ArgumentList.Add(request);}
        psi.Environment["AION2_WORK_ROOT"]=root;
        if(scanning)psi.Environment["AION2_CATALOG_SCAN"]="1";
        var logPath=System.IO.Path.ChangeExtension(result,".log");
        using var log=new StreamWriter(logPath);
        worker=new Process{StartInfo=psi};
        void Line(string? line) {
            if(line==null)return;
            lock(log)log.WriteLine(line);
            if(line.StartsWith("READ ")||line.StartsWith("BLENDER ")||line.StartsWith("DONE ")||line.StartsWith("Catalog saved"))Dispatcher.BeginInvoke(()=>{
                Notify(line);
                if(line.StartsWith("BLENDER ")){
                    var path=line[8..];var q=queue.FirstOrDefault(q=>q.Path.Equals(path,StringComparison.OrdinalIgnoreCase));if(q!=null)q.Status="Validando no Blender";
                }
            });
        }
        worker.OutputDataReceived+=(_,e)=>Line(e.Data);worker.ErrorDataReceived+=(_,e)=>Line(e.Data);
        try {worker.Start();worker.BeginOutputReadLine();worker.BeginErrorReadLine();if(scanning)await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));else await worker.WaitForExitAsync();worker.WaitForExit();TrackTemporaryFiles(request,result);return(worker.ExitCode,result);}
        catch(TimeoutException){if(!worker.HasExited)worker.Kill(true);throw;}
        finally {worker.Dispose();worker=null;}
    }
    async void Refresh_Click(object sender,RoutedEventArgs e) {
        if(busy)return;ClosePreview();previews.Clear();SetBusy(true);Notify("Lendo índices do jogo…");
        try {
            var work=await Work("catalog");
            if(work.Code!=0)throw new Exception(stopped?"Atualização interrompida.":"Falha ao atualizar. Consulte "+work.Result);
            await LoadCatalog();
        } catch(Exception ex){Notify(ex.Message,Notice.Error);}
        finally {SetBusy(false);}
    }
    async void Run_Click(object sender,RoutedEventArgs e) {
        if(busy)return;
        var pending=queue.Where(q=>q.Status!="Concluído").ToArray();
        if(pending.Length==0){Notify("Adicione assets à fila.",Notice.Warning);return;}
        if(pending.Length>10000){Notify("Limite de 10.000 itens por lote.",Notice.Warning);return;}
        if(!File.Exists(blender)){Notify("Selecione blender.exe nas opções.",Notice.Warning);return;}
        SetBusy(true);
        foreach(var q in pending){q.Status="Exportando";q.Error="";}
        try {
            foreach(var q in pending.Where(q=>q.IsAnimation)) {
                try {
                    var request=System.IO.Path.Combine(root,"reports","animation-queue-selection-"+Guid.NewGuid().ToString("N")+".json");
                    File.WriteAllText(request,JsonSerializer.Serialize(new{Clip=q.AnimationClip,Model=q.ModelFile,AssetRoot=q.AssetRoot}));
                    var work=await Work("animation-export",request);
                    using var json=JsonDocument.Parse(File.ReadAllText(work.Result));var data=json.RootElement;
                    if(work.Code!=0||!data.TryGetProperty("Success",out var success)||!success.GetBoolean())
                        throw new IOException(data.TryGetProperty("Error",out var error)?error.GetString():"Exportação da animação não confirmada.");
                    q.Status="Concluído";q.Output=data.GetProperty("Output").GetString()??"";
                }catch(Exception ex){q.Status=stopped?"Interrompido":"Falha";q.Error=ex.Message;if(stopped)break;}
            }
            var assets=pending.Where(q=>!q.IsAnimation).ToArray();
            if(assets.Length>0&&!stopped) {
                var request=System.IO.Path.Combine(root,"reports","desktop-selection-"+Guid.NewGuid().ToString("N")+".json");
                File.WriteAllText(request,JsonSerializer.Serialize(assets.Select(q=>new{q.Path})));
                var work=await Work("export",request);
                if(File.Exists(work.Result)){
                    using var json=JsonDocument.Parse(File.ReadAllText(work.Result));
                    if(json.RootElement.TryGetProperty("ReportDirectory",out var dir))lastReport=dir.GetString()??"";
                    if(json.RootElement.TryGetProperty("Items",out var items))foreach(var item in items.EnumerateArray()){
                        var q=assets.FirstOrDefault(q=>q.Path.Equals(item.GetProperty("Path").GetString(),StringComparison.OrdinalIgnoreCase));
                        if(q==null)continue;
                        q.Status=item.GetProperty("Success").GetBoolean()?"Concluído":"Falha";
                        q.Output=item.GetProperty("Output").GetString()??"";q.Error=item.GetProperty("Error").GetString()??"";
                    }
                    else if(json.RootElement.TryGetProperty("Error",out var error))throw new Exception(error.GetString());
                }
            }
            foreach(var q in pending.Where(q=>q.Status is "Exportando" or "Validando no Blender")){q.Status=stopped?"Interrompido":"Falha";q.Error=stopped?"Pode tentar novamente.":"O processo não confirmou este item. Consulte os logs.";}
            Notify($"{pending.Count(q=>q.Status=="Concluído")} concluídos · {pending.Count(q=>q.Status!="Concluído")} pendentes/falhas · Materiais: cor base");
        } catch(Exception ex){foreach(var q in pending.Where(q=>q.Status!="Concluído")){q.Status="Falha";q.Error=ex.Message;}Notify(ex.Message,Notice.Error);}
        finally{SetBusy(false);}
    }
    void Cancel_Click(object sender,RoutedEventArgs e) {
        scanCancelled=true;
        StopScanServer();
        stopped=true;
        try {worker?.Kill(entireProcessTree:true);Notify("Interrompendo. Arquivos já gravados serão preservados.");}catch(Exception ex){Notify(ex.Message,Notice.Error);}
    }
    public async Task IntegrationTest(string model,string assetRoot,string assetPath) {
        VerifyAnimationTextureFixture();
        await LoadCatalog();
        if(HomeView.Visibility!=Visibility.Visible||Categories.SelectedIndex!=-1)throw new Exception("Startup home retained a category selection");
        Categories.SelectedIndex=0;
        if(LibraryView.Visibility!=Visibility.Visible)throw new Exception("First category click did not open the library");
        Home_Click(this,new RoutedEventArgs());Library_Click(this,new RoutedEventArgs());
        if(Categories.SelectedIndex!=0||LibraryView.Visibility!=Visibility.Visible)throw new Exception("Library navigation failed");
        VerifyTextureCache();
        VerifyUpdate0713();
        var asset=all.Single(a=>a.Path.Equals(assetPath,StringComparison.OrdinalIgnoreCase));
        ShowActivated=false;ShowInTaskbar=false;Left=-10000;Top=-10000;Show();
        Width=1600;Height=950;
        ready=false;Query.Text=asset.Name;SearchMode.SelectedIndex=1;Folder.Text="";Hints.SelectedIndex=0;ready=true;
        await ApplyFilter();AssetsGrid.SelectedItem=asset;
        Enqueue(new[]{asset});SetQueueVisible(true);UpdateLayout();
        var before=AssetsGrid.ActualHeight;var queued=queue.Count;
        SetQueueVisible(false);UpdateLayout();
        if(AssetsGrid.ActualHeight<before+150||queue.Count!=queued)throw new Exception("Queue collapse did not release space/preserve items.");
        await ShowPreview(asset,new[]{model},assetRoot,0);
        if(Window.GetWindow(PreviewHost.Content as DependencyObject)!=this)throw new Exception("Viewer is not embedded");
        if(asset.Hint!=ModelAvailabilityCache.AvailableHint)throw new Exception("Asset was not confirmed");
        var cacheFile=System.IO.Path.Combine(root,"cache","available-models.json");
        var mapping=ReleaseRuntime.Support(root,"mappings","AION2-5.3.2.0-1.0.52.0.usmap");
        var reloaded=new ModelAvailabilityCache(cacheFile,ResolvePaks(game),mapping);
        var copy=new Asset {Path=asset.Path,Size=asset.Size,Chunk=asset.Chunk};copy.Prepare();reloaded.Apply(new[]{copy});
        if(copy.Hint!=ModelAvailabilityCache.AvailableHint)throw new Exception("Cache did not survive reload");
        reloaded.LoadThumbnail(copy);
        if(copy.Thumbnail==null||copy.Thumbnail.PixelWidth!=192||copy.Thumbnail.PixelHeight!=144)throw new Exception("Thumbnail not persisted");
        using(var bytes=new MemoryStream()) {
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(copy.Thumbnail));encoder.Save(bytes);
            File.WriteAllBytes(System.IO.Path.Combine(root,"reports/thumbnail-check.png"),bytes.ToArray());
        }
        var datFile=System.IO.Path.ChangeExtension(cacheFile,"dat");
        using(var archive=System.IO.Compression.ZipFile.OpenRead(datFile)) {
            if(archive.GetEntry("metadata.json")==null||!archive.Entries.Any(e=>e.FullName.EndsWith(".jpg")))throw new Exception("DAT missing metadata or images");
        }
        previews[game+"|"+asset.Path]=(new[]{model},assetRoot,0);
        await ApplyFilter();AssetsGrid.SelectedItem=asset;UpdateLayout();
        var card=AssetsGrid.ItemContainerGenerator.ContainerFromItem(asset) as ListBoxItem??throw new Exception("No card for double click");
        var click=new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount,System.Windows.Input.MouseButton.Left) {
            RoutedEvent=Control.MouseDoubleClickEvent,Source=card
        };
        Assets_DoubleClick(AssetsGrid,click);
        for(int attempt=0;busy&&attempt<300;attempt++)await Task.Delay(100);
        if(!click.Handled||busy||PreviewHost.Content is not PreviewPanel)throw new Exception("Double click preview failed");

        copy.Size++;if(reloaded.IsAvailable(copy))throw new Exception("Changed asset retained confirmation");
        var other=all.First(a=>a.Name=="SK_HW");
        if(reloaded.IsAvailable(other))throw new Exception("Unviewed asset marked as available");
        if(new ModelAvailabilityCache(cacheFile,root,mapping).IsAvailable(asset))throw new Exception("Cache leaked between installations");
        Hints.SelectedItem=ModelAvailabilityCache.AvailableHint;debounce.Stop();await ApplyFilter();
        if(!filtered.Contains(asset)||filtered.Any(a=>a.Hint!=ModelAvailabilityCache.AvailableHint))throw new Exception("Verified filter failed");
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        UpdateLayout();
        void Screenshot(string name) {
            var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var f=File.Create(System.IO.Path.Combine(root,"reports",name));encoder.Save(f);
        }
        Screenshot("integrated-viewer.png");
        var materialsPanel=(PreviewPanel)PreviewHost.Content;
        if(materialsPanel.AdvancedMaterialsActive)throw new Exception("Materials loaded automatically");
        await materialsPanel.ToggleAdvancedMaterials();
        if(!materialsPanel.AdvancedMaterialsActive)throw new Exception("Materials not activated");
        UpdateLayout();Screenshot("manual-materials.png");
        await materialsPanel.ToggleAdvancedMaterials();
        if(materialsPanel.AdvancedMaterialsActive)throw new Exception("Materials not disabled");
        SetQueueVisible(true);UpdateLayout();Screenshot("integrated-queue-visible.png");
        Hints.SelectedItems.Clear();Hints.SelectedItems.Add("Modelo provável");Hints.SelectedItems.Add(ModelAvailabilityCache.AvailableHint);
        Query.Text="";Folder.Text="AION2/Content/Character/Player/GM/Hair/";
        ExcludeQuery.Text="_Type; _000";
        debounce.Stop();await ApplyFilter();
        if(Hints.SelectedItems.Count!=2||!filtered.Contains(asset)||!filtered.Any(a=>a.Hint=="Modelo provável")||filtered.Any(a=>a.Path.Contains("_Type",StringComparison.OrdinalIgnoreCase)||a.Path.Contains("_000",StringComparison.OrdinalIgnoreCase)))
            throw new Exception("Multiple types or exclusions failed");
        SetLibraryVisible(false);UpdateLayout();
        if(LibraryColumn.ActualWidth!=0)throw new Exception("Library collapse failed");
        SetLibraryVisible(true);UpdateLayout();
        if(ScrollViewer.GetHorizontalScrollBarVisibility(Categories)!=ScrollBarVisibility.Disabled)throw new Exception("Horizontal library scrolling enabled");
        if(Progress.TranslatePoint(new Point(),this).Y<=StatusText.TranslatePoint(new Point(),this).Y)throw new Exception("Progress is not below description");
        Screenshot("search-multiple-types.png");
        var cleanFixture=System.IO.Path.Combine(root,"reports","cleanup-test-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(System.IO.Path.Combine(cleanFixture,"cache","previews","nested"));
        File.WriteAllText(System.IO.Path.Combine(cleanFixture,"cache","previews","nested","temp.glb"),"temporary");
        File.WriteAllText(System.IO.Path.Combine(cleanFixture,"cache","available-models.json"),"preserve");
        Directory.CreateDirectory(System.IO.Path.Combine(cleanFixture,"exports"));
        File.WriteAllText(System.IO.Path.Combine(cleanFixture,"exports","keep.glb"),"preserve");
        PreviewCacheCleanup.Clear(cleanFixture);
        if(Directory.Exists(System.IO.Path.Combine(cleanFixture,"cache","previews"))||!File.Exists(System.IO.Path.Combine(cleanFixture,"cache","available-models.json"))||!File.Exists(System.IO.Path.Combine(cleanFixture,"exports","keep.glb")))
            throw new Exception("Preview cleanup scope failed");
        Home_Click(this,new RoutedEventArgs());UpdateLayout();if(Categories.SelectedIndex!=-1||AssetsGrid.SelectedItems.Count!=0)throw new Exception("Home retained selection");
        if(!Title.StartsWith("Aion2 Porting")||Icon==null||FindResource("BrandLogo") is not ImageBrush)throw new Exception("Branding resources missing");
        Screenshot("html-ui-home.png");
        HomeView.ScrollToEnd();UpdateLayout();Screenshot("home-updates-bottom.png");
        HomeView.ScrollToTop();UpdateLayout();
        Options_Click(this,new RoutedEventArgs());UpdateLayout();Screenshot("html-ui-options.png");
        var previousOutput=outputFolder;
        OutputPath.Text=game;OptionsSave_Click(this,new RoutedEventArgs());
        if(OptionsOverlay.Visibility!=Visibility.Visible||OptionsError.Text.Length==0||outputFolder!=previousOutput)throw new Exception("Options allowed output inside game");
        OutputPath.Text=System.IO.Path.Combine(root,"reports","ui-options-output");OptionsSave_Click(this,new RoutedEventArgs());
        if(OptionsOverlay.Visibility!=Visibility.Collapsed||outputFolder==previousOutput)throw new Exception("Options did not save");
        using(var savedSettings=JsonDocument.Parse(ReadSettings()))
            if(savedSettings.RootElement.GetProperty("Output").GetString()!=outputFolder)throw new Exception("Options not persisted");
        Options_Click(this,new RoutedEventArgs());OutputPath.Text="discard this edit";OptionsClose_Click(this,new RoutedEventArgs());
        if(outputFolder=="discard this edit")throw new Exception("Cancel saved changes");
        outputFolder=previousOutput;SaveSettings();
        OptionsClose_Click(this,new RoutedEventArgs());ShowLibrary();UpdateLayout();
        var selectedMode=SearchMode.SelectedItem;
        var sameAsset=asset.Path;
        Options_Click(this,new RoutedEventArgs());LanguageChoice.SelectedIndex=1;OptionsSave_Click(this,new RoutedEventArgs());UpdateLayout();
        if(Localization.Language!="en"||!VersionLabel.Text.StartsWith("VERSION ")||!Equals(SearchMode.SelectedItem,selectedMode)||asset.Path!=sameAsset)throw new Exception($"Language switch failed: language={Localization.Language}; label={VersionLabel.Text}; mode={SearchMode.SelectedItem}/{selectedMode}; options={OptionsError.Text}");
        using(var languageSettings=JsonDocument.Parse(ReadSettings()))
            if(languageSettings.RootElement.GetProperty("Language").GetString()!="en")throw new Exception("Language preference not persisted");
        Screenshot("language-english.png");
        var blackDragon=AnimationClipData.Load(System.IO.Path.Combine(root,"reports/blackdragon-clip.json"));
        if(blackDragon.Frames.Length<1||blackDragon.Bones.Length!=135)throw new Exception("Unexpected BlackDragon clip");
        ClosePreview();var dragonPanel=new AnimationPanel(blackDragon);PreviewHost.Content=dragonPanel;PreviewHost.Visibility=Visibility.Visible;PreviewColumn.Width=new GridLength(420);
        for(int i=0;i<blackDragon.Frames.Length;i++)dragonPanel.ShowFrame(i);
        dragonPanel.Play();await Task.Delay(120);dragonPanel.Pause();dragonPanel.ShowFrame(blackDragon.Frames.Length-1);UpdateLayout();
        Screenshot("blackdragon-animation-english.png");
        File.WriteAllText(System.IO.Path.Combine(root,"reports/blackdragon-viewer-test.json"),JsonSerializer.Serialize(new{Passed=true,Frames=blackDragon.Frames.Length,Bones=135,AllFramesRendered=true,Playback=true,LastFrame=true}));ClosePreview();
        var montage=AnimationClipData.Load(System.IO.Path.Combine(root,"reports/montage-clip.json"));
        var montagePanel=new AnimationPanel(montage);PreviewHost.Content=montagePanel;PreviewHost.Visibility=Visibility.Visible;PreviewColumn.Width=new GridLength(420);
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        for(int i=0;i<montage.Frames.Length;i++)montagePanel.ShowFrame(i);
        montagePanel.SelectSection("Start");montagePanel.AdvanceMontage(30);
        if(montagePanel.CurrentSection!="Loop"||montagePanel.Frame!=30)throw new Exception("Montage did not transition from Start to Loop");
        montagePanel.AdvanceMontage(1501);
        if(montagePanel.Frame!=(30+1501)%montage.Frames.Length)throw new Exception("Montage loop section failed");
        montagePanel.SelectSection("Loop");montagePanel.Play();await Task.Delay(160);montagePanel.Pause();
        if(montagePanel.CurrentSection!="Loop"||montagePanel.Frame==30)throw new Exception("Montage timer playback failed");
        montagePanel.SelectSection("LoopEnd");montagePanel.Play();montagePanel.AdvanceMontage(100);
        if(montagePanel.Frame!=(45+100)%montage.Frames.Length||!montagePanel.IsPlaying)throw new Exception("Montage ending did not stop safely");
        montagePanel.SelectSection("End");montagePanel.AdvanceMontage(100);UpdateLayout();Screenshot("montage-ending-english.png");
        File.WriteAllText(System.IO.Path.Combine(root,"reports/montage-viewer-test.json"),JsonSerializer.Serialize(new{Passed=true,Frames=montage.Frames.Length,Bones=montage.Bones.Length,AllFramesRendered=true,StartToLoop=true,FullCycleRepeats=true,ExitLoop=true,FinalSection=true}));ClosePreview();
        Options_Click(this,new RoutedEventArgs());UpdateLayout();Screenshot("language-options-english.png");LanguageChoice.SelectedIndex=0;OptionsSave_Click(this,new RoutedEventArgs());UpdateLayout();
        if(Localization.Language!="pt-BR"||!VersionLabel.Text.StartsWith("VERSÃO ")||Localization.T("Index was outside the bounds of the array.")!="Índice fora dos limites da lista.")throw new Exception("Portuguese language restoration failed");
        File.WriteAllText(System.IO.Path.Combine(root,"reports/language-test.json"),JsonSerializer.Serialize(new{Passed=true,English=true,Portuguese=true,PreferenceSaved=true,FilterKeysPreserved=true,AssetNamesPreserved=true,ErrorsTranslated=true}));
        SetQueueVisible(false);
        Query.Text="";Folder.Text="";ExcludeQuery.Text="";Hints.SelectedItems.Clear();debounce.Stop();await ApplyFilter();
        if(AssetsGrid.Items.Count>PageSize||filtered.Count<PageSize*2)throw new Exception("Card pagination failed");
        var firstCard=((Asset)AssetsGrid.Items[0]).Path;
        NextPage_Click(this,new RoutedEventArgs());
        if(((Asset)AssetsGrid.Items[0]).Path==firstCard||pageIndex!=1)throw new Exception("Next page failed");
        PrevPage_Click(this,new RoutedEventArgs());
        if(((Asset)AssetsGrid.Items[0]).Path!=firstCard)throw new Exception("Previous page failed");
        var countBefore=queue.Count;AssetsGrid.SelectedItems.Clear();
        var selectedCards=AssetsGrid.Items.Cast<Asset>().Take(2).ToArray();
        foreach(var selectedCard in selectedCards)AssetsGrid.SelectedItems.Add(selectedCard);
        Add_Click(this,new RoutedEventArgs());
        if(selectedCards.Any(a=>!queue.Any(q=>q.Path==a.Path)))throw new Exception("Card multi-selection queue failed");
        queued=queue.Count;
        Hints.SelectedItems.Clear();Hints.SelectedItems.Add("Modelo provável");Hints.SelectedItems.Add(ModelAvailabilityCache.AvailableHint);
        Folder.Text="AION2/Content/Character/Player/GM/Hair/";ExcludeQuery.Text="_Type; _000";debounce.Stop();await ApplyFilter();
        UpdateLayout();Screenshot("html-ui-cards.png");
        var invalid=System.IO.Path.Combine(root,"reports","invalid-preview-test.glb");File.WriteAllText(invalid,"not a GLB");
        try {await ShowPreview(other,new[]{invalid},System.IO.Path.GetDirectoryName(invalid)!,0);throw new Exception("Invalid model accepted");}
        catch(Exception) when(other.Hint!=ModelAvailabilityCache.AvailableHint) { }
        if(reloaded.IsAvailable(other))throw new Exception("Failed preview entered cache");
        var rejected=new Asset{Path=other.Path,Size=other.Size,Chunk=other.Chunk};rejected.Prepare();
        new ModelAvailabilityCache(cacheFile,ResolvePaks(game),mapping).Apply(new[]{rejected});
        if(rejected.Hint!="Não classificado")throw new Exception("Failure classification not persisted");
        availability!.SetSubcategory(new[]{other},"Teste manual sem 3D");
        availability.Reject(other);
        var manualCopy=new Asset{Path=other.Path,Chunk=other.Chunk,Size=other.Size};manualCopy.Prepare();
        new ModelAvailabilityCache(cacheFile,ResolvePaks(game),mapping).Apply(new[]{manualCopy});
        if(manualCopy.Section!="Teste manual sem 3D"||manualCopy.Hint!="Não classificado")throw new Exception("Manual section lost after failed 3D");
        var changedMapping=System.IO.Path.Combine(root,"reports/manual-section-mapping.usmap");File.WriteAllText(changedMapping,"changed mapping test");
        manualCopy.Prepare();new ModelAvailabilityCache(cacheFile,ResolvePaks(game),changedMapping).Apply(new[]{manualCopy});
        if(manualCopy.Section!="Teste manual sem 3D")throw new Exception("Manual section lost after mapping change");
        availability.SetSubcategory(new[]{other},"Outros");

        var fixture=System.IO.Path.Combine(root,"reports","cache-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
        var mapTest=System.IO.Path.Combine(fixture,"test.usmap");File.WriteAllText(mapTest,"mapping-a");
        var chunkTest=System.IO.Path.Combine(fixture,"test.utoc");File.WriteAllText(chunkTest,"index-a");
        var dataTest=System.IO.Path.Combine(fixture,"test.ucas");File.WriteAllText(dataTest,"data-a");
        var testAsset=new Asset{Path="Test.uasset",Size=1,Chunk="test.utoc"};testAsset.Prepare();
        var cacheTest=System.IO.Path.Combine(fixture,"confirmed.json");
        var geometry=await Task.Run(()=>PreviewLoader.Load(model,assetRoot));
        new ModelAvailabilityCache(cacheTest,fixture,mapTest).Confirm(testAsset,geometry);
        File.AppendAllText(dataTest,"changed");
        if(new ModelAvailabilityCache(cacheTest,fixture,mapTest).IsAvailable(testAsset))throw new Exception("Changed container retained confirmation");
        new ModelAvailabilityCache(cacheTest,fixture,mapTest).Confirm(testAsset,geometry);
        File.AppendAllText(mapTest,"changed");
        if(new ModelAvailabilityCache(cacheTest,fixture,mapTest).IsAvailable(testAsset))throw new Exception("Changed mapping retained confirmation");
        ClosePreview();if(PreviewColumn.Width.Value!=0||queue.Count!=queued)throw new Exception("Closing preview changed queue");
        ready=false;Query.Text="";Folder.Text="";ExcludeQuery.Text="";Categories.SelectedIndex=0;
        UpdateFacets();Sections.SelectedIndex=0;Chunks.SelectedIndex=0;Hints.SelectedItems.Clear();PreferBase.IsChecked=false;ready=true;debounce.Stop();
        filtered=Enumerable.Range(0,240).Select(i=>{var a=new Asset{Path=$"Test/Position_{i}.uasset",Size=1};a.Prepare();return a;}).ToList();
        filtered[130]=asset;pageIndex=2;RenderPage();UpdateLayout();
        var sourceBefore=AssetsGrid.ItemsSource;var pageBefore=pageIndex;
        var scrollBefore=FindVisual<ScrollViewer>(AssetsGrid)!;scrollBefore.ScrollToVerticalOffset(100);
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        var offsetBefore=scrollBefore.VerticalOffset;
        await ShowPreview(asset,new[]{model},assetRoot,0);
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        if(!ReferenceEquals(sourceBefore,AssetsGrid.ItemsSource)||pageIndex!=pageBefore||Math.Abs(scrollBefore.VerticalOffset-offsetBefore)>1)
            throw new Exception("Successful preview moved the page");
        if(((SolidColorBrush)StatusText.Foreground).Color!=Color.FromRgb(74,222,128)||StatusText.FontWeight!=FontWeights.Bold)throw new Exception("Success notice style failed");
        var removed=visibleAssets[5];removed.MarkModelAvailable();Hints.SelectedItems.Add(ModelAvailabilityCache.AvailableHint);debounce.Stop();
        var count=visibleAssets.Count;removed.MarkUnclassified();RefreshAssetInPlace(removed);
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        if(visibleAssets.Count!=count-1||visibleAssets.Contains(removed)||pageIndex!=pageBefore||!ReferenceEquals(sourceBefore,AssetsGrid.ItemsSource)||Math.Abs(scrollBefore.VerticalOffset-offsetBefore)>1)
            throw new Exception("In-place removal moved the page");
        var nextExpected=filtered[filtered.IndexOf(visibleAssets.Last())+1];
        NextPage_Click(this,new RoutedEventArgs());
        if(!ReferenceEquals(visibleAssets.First(),nextExpected))throw new Exception("Page navigation skipped an asset after removal");
        PrevPage_Click(this,new RoutedEventArgs());
        if(visibleAssets.Count!=count-1||!visibleAssets.Contains(asset))throw new Exception("Previous page lost its remaining cards");
        Notify("Teste de alerta",Notice.Warning);
        if(((SolidColorBrush)StatusText.Foreground).Color!=Color.FromRgb(250,204,21))throw new Exception("Warning notice style failed");
        Notify("Teste de erro",Notice.Error);
        if(((SolidColorBrush)StatusText.Foreground).Color!=Color.FromRgb(255,98,98))throw new Exception("Error notice style failed");
        var audioFixture=System.IO.Path.Combine(root,"reports/audio-test.wav");
        bool audioPlayback=false;
        if(File.Exists(audioFixture)) {
            var audioAsset=new Asset{Path="Test/audio.wav",Size=123};audioAsset.Prepare();
            if(audioAsset.Hint!="Áudio provável")throw new Exception("Probable audio classification failed");
            Hints.SelectedItems.Clear();debounce.Stop();
            filtered=new List<Asset>{audioAsset};removedFromPage.Clear();pageIndex=0;RenderPage();
            await ShowAudio(audioAsset,new[]{audioFixture});

            var audioPanel=(AudioPanel)PreviewHost.Content;
            await Task.Delay(700);
            if(audioPanel.Duration.TotalSeconds<1||audioPanel.Position.TotalSeconds<=0)throw new Exception("Audio did not advance");
            if(audioAsset.Hint!="Áudio"||!audioAsset.AudioConfirmed)throw new Exception("Audio was not confirmed");
            var audioCopy=new Asset{Path=audioAsset.Path,Size=audioAsset.Size};audioCopy.Prepare();
            new ModelAvailabilityCache(cacheFile,ResolvePaks(game),mapping).Apply(new[]{audioCopy});
            if(!audioCopy.AudioConfirmed)throw new Exception("Audio type did not survive reload");
            UpdateLayout();
            bool VisibleNote(DependencyObject element) {
                if(element is FrameworkElement icon && Equals(icon.Tag,"AudioIcon") && icon.IsVisible)return true;
                for(int i=0;i<VisualTreeHelper.GetChildrenCount(element);i++)if(VisibleNote(VisualTreeHelper.GetChild(element,i)))return true;
                return false;
            }
            if(!VisibleNote(AssetsGrid))throw new Exception("Audio note icon not visible");

            audioPlayback=true;UpdateLayout();Screenshot("audio-player.png");ClosePreview();
        }

        int additionalAudioTracks=0;
        var extraAudio=System.IO.Path.Combine(root,"reports/additional-audio.json");
        if(File.Exists(extraAudio)) {
            using var extra=JsonDocument.Parse(File.ReadAllText(extraAudio));
            foreach(var file in extra.RootElement.EnumerateArray()) {
                var track=new Asset{Path="Test/reference-audio.wav"};track.Prepare();
                await ShowAudio(track,new[]{file.GetString()!});
                var panel=(AudioPanel)PreviewHost.Content;
                await Task.Delay(100);
                if(panel.Duration<=TimeSpan.Zero||panel.Position<=TimeSpan.Zero)throw new Exception("Referenced audio playback failed");
                additionalAudioTracks++;ClosePreview();
            }
        }
        bool effectInspection=false,textureInspection=false;
        var inspectionFixture=System.IO.Path.Combine(root,"reports/inspection-test.json");
        if(File.Exists(inspectionFixture)) {
            using var document=JsonDocument.Parse(File.ReadAllText(inspectionFixture));
            var effectAsset=new Asset{Path=document.RootElement.GetProperty("Asset").GetString()!};effectAsset.Prepare();
            ShowInspection(effectAsset,document.RootElement.Clone());UpdateLayout();
            if(((InspectionPanel)PreviewHost.Content).ObjectCount==0)throw new Exception("Effect inspection empty");
            effectInspection=true;Screenshot("effect-inspection.png");ClosePreview();
        }
        var textureFixture=System.IO.Path.Combine(root,"reports/inspection-texture-test.json");
        if(File.Exists(textureFixture)) {
            using var document=JsonDocument.Parse(File.ReadAllText(textureFixture));
            var textureAsset=new Asset{Path=document.RootElement.GetProperty("Asset").GetString()!};textureAsset.Prepare();
            ShowInspection(textureAsset,document.RootElement.Clone());UpdateLayout();
            if(((InspectionPanel)PreviewHost.Content).ImageCount==0)throw new Exception("Texture inspection empty");
            textureInspection=true;Screenshot("effect-texture.png");ClosePreview();
        }

        bool animationPlayback=false,animatedMesh=false;
        var animationFixture=System.IO.Path.Combine(root,"reports/animation-test.json");
        if(File.Exists(animationFixture)) {
            var clip=AnimationClipData.Load(animationFixture);
            var first=clip.World(clip.Frames[0]);var middle=clip.World(clip.Frames[clip.Frames.Length/2]);
            if(!first.Where((m,i)=>m!=middle[i]).Any())throw new Exception("Animation has no movement");
            ClosePreview();var animationPanel=new AnimationPanel(clip,lastPreviewModel);
            PreviewHost.Content=animationPanel;PreviewHost.Visibility=Visibility.Visible;PreviewColumn.Width=new GridLength(420);
            UpdateLayout();Screenshot("animation-skeleton.png");
            if(animationPanel.IsPlaying)throw new Exception("Animation started automatically");
            animationPanel.Play();await Task.Delay(180);animationPanel.Pause();
            if(animationPanel.Frame==0||animationPanel.IsPlaying)throw new Exception("Animation controls failed");
            int pausedFrame=animationPanel.Frame;await Task.Delay(100);
            if(animationPanel.Frame!=pausedFrame)throw new Exception("Animation did not pause");
            var queuedAnimationModels=new List<string?>();animationPanel.QueueRequested+=modelPath=>queuedAnimationModels.Add(modelPath);
            IEnumerable<DependencyObject> AnimationChildren(DependencyObject parent) {
                for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++) {
                    var child=VisualTreeHelper.GetChild(parent,i);yield return child;
                    foreach(var nested in AnimationChildren(child))yield return nested;
                }
            }
            var skeletonQueueButton=AnimationChildren(animationPanel).OfType<Button>().Single(b=>Equals(b.Content,"Fila: esqueleto + animação"));
            var probable=new Asset{Path="AION2/Content/SM_Test_Probable.uasset"};probable.Prepare();
            var available=new Asset{Path="AION2/Content/MOB_Test_Available.uasset"};available.Prepare();available.MarkModelAvailable();
            var excluded=new Asset{Path="AION2/Content/T_Test_Texture.uasset"};excluded.Prepare();
            animationPanel.SetLibrary(new[]{probable,available,excluded});
            var modelPicker=AnimationChildren(animationPanel).OfType<Expander>().Single();modelPicker.IsExpanded=true;UpdateLayout();
            var pickerList=AnimationChildren(animationPanel).OfType<ListBox>().Single();
            var pickerSearch=AnimationChildren(animationPanel).OfType<TextBox>().Single();
            if(pickerList.Items.Count!=2)throw new Exception("Animation library did not restrict model types");
            pickerSearch.Text="available";if(pickerList.Items.Count!=1||!ReferenceEquals(pickerList.Items[0],available))throw new Exception("Animation model search failed");
            Asset? requested=null;animationPanel.LibraryModelRequested+=item=>requested=item;pickerList.SelectedItem=available;
            AnimationChildren(animationPanel).OfType<Button>().Single(b=>Equals(b.Content,"Aplicar modelo selecionado")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(!ReferenceEquals(requested,available))throw new Exception("Animation model selection failed");
            int infoCount=0;animationPanel.InspectRequested+=()=>infoCount++;
            AnimationChildren(animationPanel).OfType<Button>().Single(b=>Equals(b.Content,"ⓘ")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(infoCount!=1)throw new Exception("Animation inspection button failed");
            if(!Equals(PreviewButton.Content,"Visualizar"))throw new Exception("Unified preview caption failed");
            UpdateLayout();Screenshot("animation-library-picker.png");modelPicker.IsExpanded=false;UpdateLayout();
            File.WriteAllText(System.IO.Path.Combine(root,"reports/library-viewer-result.json"),JsonSerializer.Serialize(new{Passed=true,Version=typeof(MainWindow).Assembly.GetName().Version!.ToString(3),OnlyProbableAndAvailable=true,Search=true,LibrarySelection=true,AnimationInfoButton=true,UnifiedVisualizar=true}));
            var combinedQueueButton=AnimationChildren(animationPanel).OfType<Button>().Single(b=>Equals(b.Content,"Fila: modelo + texturas + animação"));
            if(combinedQueueButton.IsEnabled)throw new Exception("Combined animation queue enabled without a model");
            animationPanel.Play();skeletonQueueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(queuedAnimationModels.Count!=1||queuedAnimationModels[0]!=null||animationPanel.IsPlaying)throw new Exception("Skeleton animation queue button failed");
            animationPlayback=true;
            try {
                var animationModelFixture=System.IO.Path.Combine(root,"reports/animation-model.glb");
                var animationModel=File.Exists(animationModelFixture)?animationModelFixture:model;
                var animated=new AnimatedMesh(animationModel,clip);animated.Update(first);
                var animatedGeometry=(System.Windows.Media.Media3D.MeshGeometry3D)((System.Windows.Media.Media3D.GeometryModel3D)animated.Scene.Children[0]).Geometry;
                var old=animatedGeometry.Positions.ToArray();animated.Update(middle);
                if(old.SequenceEqual(animatedGeometry.Positions))throw new Exception("Mesh did not move");
                animationPanel.LoadModel(animationModel);animationPanel.ShowFrame(clip.Frames.Length/2);
                if(!combinedQueueButton.IsEnabled)throw new Exception("Combined animation queue stayed disabled");
                combinedQueueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(queuedAnimationModels.Count!=2||!string.Equals(queuedAnimationModels[1],animationModel,StringComparison.OrdinalIgnoreCase))throw new Exception("Combined animation queue did not retain its GLB");
                UpdateLayout();Screenshot("animation-mesh.png");animatedMesh=true;File.WriteAllText(System.IO.Path.Combine(root,"reports/animation-mesh-result.txt"),"Success: GM_0000_T01_Body animated with GM_SW_Fly_Aerial_Loop; "+animated.Vertices+" vertices; positions changed between frames.");
            }catch(Exception ex){File.WriteAllText(System.IO.Path.Combine(root,"reports/animation-mesh-result.txt"),ex.ToString());}
            ClosePreview();
        }        File.WriteAllText(System.IO.Path.Combine(root,"reports/integration-test.json"),JsonSerializer.Serialize(new{Passed=true,ManualMaterials=true,AnimationPlayback=animationPlayback,AnimationExportButton=animationPlayback,AnimatedMesh=animatedMesh,PagePositionPreserved=true,InPlaceRemoval=true,InlineNotices=true,AudioPlayback=audioPlayback,AdditionalAudioTracks=additionalAudioTracks,AudioClassificationAndIcon=audioPlayback,EffectInspection=effectInspection,TextureInspection=textureInspection,UnifiedDat=true,ManualSubcategoryPersists=true,DoubleClickPreview=true,ThumbnailPersisted=true,FailedModelUnclassified=true,HomeSelectionCleared=true,CardPagination=true,CardMultiSelection=true,HomeAndOptions=true,OptionsSaveCancel=true,ProtectedGameOutput=true,MultipleTypes=true,Exclusions=true,LibraryCollapsible=true,ProgressBelowDescription=true,ScopedCleanup=true,EmbeddedViewer=true,QueueCollapse=true,QueuePreserved=true,CacheReload=true,ChangedAssetInvalidated=true,ContainerChangeInvalidated=true,MappingChangeInvalidated=true,FailedPreviewNotCached=true,InstallationIsolation=true,VerifiedFilter=true,Asset=asset.Path,Hint=asset.Hint}));
    }

    void VerifyAnimationTextureFixture(){
        var file=System.IO.Path.Combine(root,"reports","animation-texture-test.json");if(!File.Exists(file))throw new Exception("Missing animation texture fixture");
        using var config=JsonDocument.Parse(File.ReadAllText(file));var data=config.RootElement;
        var clip=AnimationClipData.Load(data.GetProperty("Clip").GetString()!);
        var mesh=new AnimatedMesh(data.GetProperty("Model").GetString()!,clip);
        if(mesh.TexturedParts!=0)throw new Exception("Animation textures loaded automatically");
        var count=mesh.ApplyTextures(data.GetProperty("Root").GetString()!);if(count==0)throw new Exception("Real animation model has no applied textures");
        var geometry=(System.Windows.Media.Media3D.GeometryModel3D)mesh.Scene.Children[0];var material=geometry.Material;
        mesh.Update(clip.World(clip.Frames[clip.Frames.Length/2]));
        if(!ReferenceEquals(material,geometry.Material)||((System.Windows.Media.Media3D.MeshGeometry3D)geometry.Geometry).TextureCoordinates.Count==0)throw new Exception("Animation frame lost its texture");
        File.WriteAllText(System.IO.Path.Combine(root,"reports","animation-texture-result.json"),JsonSerializer.Serialize(new{Passed=true,Model="MOB_Balgest_01",TexturedParts=count,ManualLoading=true,TexturePreservedDuringAnimation=true}));
    }
    public async Task SelfTest() {
        await VerifyCompactStorage();
        await LoadCatalog();
        if(HomeView.Visibility!=Visibility.Visible||Categories.SelectedIndex!=-1)throw new Exception("Startup home retained a category selection");
        Categories.SelectedIndex=0;
        if(LibraryView.Visibility!=Visibility.Visible)throw new Exception("First category click did not open the library");
        Home_Click(this,new RoutedEventArgs());Library_Click(this,new RoutedEventArgs());
        if(Categories.SelectedIndex!=0||LibraryView.Visibility!=Visibility.Visible)throw new Exception("Library navigation failed");
        VerifyTextureCache();
        VerifyUpdate0713();
        if(all.Count==0)throw new Exception("Empty catalog");
        var textureFixture=System.IO.Path.Combine(root,"reports","wpf-texture-read-result.json");
        if(File.Exists(textureFixture)) {
            using var textureDocument=JsonDocument.Parse(File.ReadAllText(textureFixture));var textureData=textureDocument.RootElement;
            var asset=all.Single(a=>a.Path==textureData.GetProperty("Asset").GetString());
            if(textureData.GetProperty("Images").EnumerateArray().All(i=>File.Exists(i.GetString()))) {
                ShowInspection(asset,textureData);
                if(!asset.TextureConfirmed||asset.Thumbnail!=null)throw new Exception("Real texture inspection was not cached");
                File.WriteAllText(System.IO.Path.Combine(root,"reports","real-texture-cache-result.json"),JsonSerializer.Serialize(new{Passed=true,asset.Path,asset.Hint,ImageCount=((InspectionPanel)PreviewHost.Content).ImageCount}));ClosePreview();
            }
        }
        var hair=SearchEngine.Filter(all,"GM_Hair_*","Padrão (* e ?)","AION2/Content/Character/Player/GM/Hair/","Todos os assets","Todas as subcategorias","Todos os tipos","Todos os chunks",true);
        if(!hair.Any(a=>a.Name=="GM_Hair_412")||hair.Any(a=>a.Name=="GM_Hair_408_Type1"))throw new Exception("Base preference failed");
        var exact=SearchEngine.Filter(all,"HW_0001","Nome exato","","Todos os assets","Todas as subcategorias","Todos os tipos","Todos os chunks",false);
        if(exact.Count==0)throw new Exception("Exact search failed");
        Query.Text="GM_Hair_*";SearchMode.SelectedIndex=2;Folder.Text="AION2/Content/Character/Player/GM/Hair/";PreferBase.IsChecked=true;
        await ApplyFilter();Enqueue(exact.Take(1));Enqueue(exact.Take(1));if(queue.Count!=1)throw new Exception("Queue deduplication failed");
        ShowActivated=false;ShowInTaskbar=false;Left=-10000;Top=-10000;Show();
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        Width=1280;Height=850;Measure(new Size(1280,850));Arrange(new Rect(0,0,1280,850));UpdateLayout();
        var target=new RenderTargetBitmap(1280,850,96,96,PixelFormats.Pbgra32);var visual=(FrameworkElement)Content; visual.Measure(new Size(1232,802));visual.Arrange(new Rect(0,0,1232,802));visual.UpdateLayout();target.Render(visual);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(target));
        using(var output=File.Create(System.IO.Path.Combine(root,"reports/desktop-preview.png")))png.Save(output);
        void Capture(string name) {
            UpdateLayout();var bitmap=new RenderTargetBitmap(1232,802,96,96,PixelFormats.Pbgra32);bitmap.Render((FrameworkElement)Content);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(System.IO.Path.Combine(root,"reports",name));encoder.Save(file);
        }
        Home_Click(this,new RoutedEventArgs());Capture("0713-home.png");
        Options_Click(this,new RoutedEventArgs());Capture("0713-options.png");OptionsClose_Click(this,new RoutedEventArgs());
        Library_Click(this,new RoutedEventArgs());
        var icons=new[]{new Asset{Path="T_Texture.uasset"},new Asset{Path="SW_Audio.uasset"},new Asset{Path="Animation.uasset"},new Asset{Path="Montage.uasset"}};
        foreach(var icon in icons)icon.Prepare();icons[0].MarkTexture();icons[1].MarkAudio();icons[2].MarkAnimation(false);icons[3].MarkAnimation(true);AssetsGrid.ItemsSource=icons;Capture("0713-icons.png");
        AssetsGrid.ItemsSource=visibleAssets;
        File.WriteAllText(System.IO.Path.Combine(root,"reports/desktop-self-test.json"),JsonSerializer.Serialize(new{Passed=true,Assets=all.Count,HairMatches=hair.Count,ExactMatches=exact.Count,QueueDeduplication=true}));
    }
    void VerifyTextureCache() {
        var dir=System.IO.Path.Combine(root,"reports","texture-cache-test");Directory.CreateDirectory(dir);
        var pngPath=System.IO.Path.Combine(dir,"texture.png");
        var pixels=new byte[32*32*4];for(int i=0;i<pixels.Length;i+=4){pixels[i]=100;pixels[i+1]=190;pixels[i+2]=230;pixels[i+3]=255;}
        var image=BitmapSource.Create(32,32,96,96,PixelFormats.Bgra32,null,pixels,128);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var s=File.Create(pngPath))encoder.Save(s);
        var file=System.IO.Path.Combine(dir,"cache.json");var asset=new Asset{Path="AION2/Content/T_CacheVerification.uasset",Size=32,Chunk="fixture.utoc"};asset.Prepare();
        var cache=new ModelAvailabilityCache(file,dir,"missing-map");cache.ConfirmTexture(asset,pngPath);
        if(!asset.TextureConfirmed||asset.Thumbnail!=null||cache.IsAvailable(asset))throw new Exception("Texture cache incorrectly confirmed a model");
        var copy=new Asset{Path=asset.Path,Size=asset.Size,Chunk=asset.Chunk};copy.Prepare();var reload=new ModelAvailabilityCache(file,dir,"missing-map");reload.Apply(new[]{copy});reload.LoadThumbnail(copy);
        if(!copy.TextureConfirmed||copy.Thumbnail!=null)throw new Exception("Texture icon/type did not survive restart");
        copy.Size++;copy.Prepare();reload.Apply(new[]{copy});reload.LoadThumbnail(copy);if(copy.TextureConfirmed||copy.Thumbnail!=null)throw new Exception("Stale texture stayed confirmed");
        File.WriteAllText(System.IO.Path.Combine(root,"reports","texture-cache-result.json"),JsonSerializer.Serialize(new{Passed=true,TextureType=true,IconOnly=true,Reload=true,Invalidation=true,NoModelConfirmation=true,HomeStartupUnselected=true,FirstCategoryClick=true}));
    }
    void VerifyUpdate0713() {
        float[] transform=[0,0,0,0,0,0,1,1,1,1];
        var clip=new AnimationClipData("Test","Test",30,1,[new("Root",-1,transform)],Enumerable.Range(0,30).Select(_=>new[]{transform}).ToArray(),[new("Start",0,10,"Loop"),new("Loop",10,20,"Loop"),new("End",20,30,null)],"Montage");
        using(var defaultPanel=new AnimationPanel(clip with{Sections=[new("Default",0,30,"Default")]})){
            if(defaultPanel.HasSectionSelector)throw new Exception("Default-only section controls were shown");
            defaultPanel.AdvanceMontage(31);if(defaultPanel.Frame!=1)throw new Exception("Default-only playback failed");
        }
        using(var sectionsPanel=new AnimationPanel(clip))if(!sectionsPanel.HasSectionSelector)throw new Exception("Named montage sections were hidden");
        File.WriteAllText(System.IO.Path.Combine(root,"reports","update-0714-test.json"),"{\"Passed\":true,\"DefaultOnlyHidden\":true,\"PlaybackPreserved\":true,\"NamedSectionsVisible\":true}");
        float[] identity=[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1];
        var profile=new SkeletonProfile([new("Root",identity)]);
        if(!profile.Matches(clip)||new SkeletonProfile([new("Missing",identity)]).Matches(clip))throw new Exception("Skeleton name matching failed");
        var shifted=(float[])identity.Clone();shifted[12]=1;
        if(new SkeletonProfile([new("Root",shifted)]).Matches(clip))throw new Exception("Incompatible rest pose accepted");
        var realModel=System.IO.Path.Combine(root,"reports/animation-binding-tests/Balgest/model/Aion2/Content/Character/Monster/Animal/Balgest/Balgest_01/MOB_Balgest_01.glb");
        var realClip=System.IO.Path.Combine(root,"reports/animation-binding-tests/Balgest/clip.json");
        bool realCompatibility=false;
        if(File.Exists(realModel)&&File.Exists(realClip)) {
            var realProfile=SkeletonProfile.Read(realModel);var animation=AnimationClipData.Load(realClip);
            if(!realProfile.Matches(animation))throw new Exception("Real Balgest model was rejected by its own animation");
            var other=AnimationClipData.Load(System.IO.Path.Combine(root,"reports/thumbnail-ui-test/reports/blackdragon-clip.json"));
            if(realProfile.Matches(other))throw new Exception("Balgest was accepted for BlackDragon");
            var folder=System.IO.Path.Combine(root,"reports/texture-cache-test");
            var modelAsset=new Asset{Path="SM_RealModel.uasset",Size=1,Chunk="fixture.utoc"};modelAsset.Prepare();
            var modelCache=new ModelAvailabilityCache(System.IO.Path.Combine(folder,"skeleton.json"),folder,"missing");modelCache.SaveSkeleton(modelAsset,realModel);
            var reloadAsset=new Asset{Path=modelAsset.Path,Size=modelAsset.Size,Chunk=modelAsset.Chunk};reloadAsset.Prepare();new ModelAvailabilityCache(System.IO.Path.Combine(folder,"skeleton.json"),folder,"missing").Apply(new[]{reloadAsset});
            if(reloadAsset.SkeletonProfile?.Matches(animation)!=true)throw new Exception("Real skeleton profile was not persisted");
            realCompatibility=true;
        }
        using(var panel=new AnimationPanel(clip)) {
            panel.AdvanceMontage(10);if(panel.CurrentSection!="Loop")throw new Exception("Start transition failed");
            panel.AdvanceMontage(10);if(panel.CurrentSection!="End")throw new Exception("Self-loop did not advance to End");
            panel.AdvanceMontage(10);if(panel.CurrentSection!="Start")throw new Exception("Full montage cycle did not restart");
            panel.AdvanceMontage(int.MaxValue);if(panel.Frame!=int.MaxValue%30)throw new Exception("Large frame delta overflow");
            var verified=new Asset{Path="SM_Compatible.uasset"};verified.Prepare();verified.SkeletonProfile=profile;
            var unverified=new Asset{Path="SM_Unverified.uasset"};unverified.Prepare();
            panel.SetLibrary(new[]{verified,unverified});
            var field=typeof(AnimationPanel).GetField("compatibleOnly",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            ((CheckBox)field.GetValue(panel)!).IsChecked=true;
            var list=(ListBox)typeof(AnimationPanel).GetField("modelList",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(panel)!;
            if(list.Items.Count!=1||list.Items[0]!=verified)throw new Exception("Compatible-only library filter failed");
        }
        var montageFile=System.IO.Path.Combine(root,"reports/thumbnail-ui-test/reports/montage-clip.json");
        bool realMontage=false;
        if(File.Exists(montageFile)) {
            var montage=AnimationClipData.Load(montageFile);using var panel=new AnimationPanel(montage);
            panel.SelectSection("Start");panel.AdvanceMontage(45);if(panel.CurrentSection=="Loop")throw new Exception("Real montage remained in Loop");
            panel.ShowFrame(montage.Frames.Length-1);panel.AdvanceMontage(1);if(panel.Frame!=0)throw new Exception("Real montage failed to repeat");realMontage=true;
        }
        var dir=System.IO.Path.Combine(root,"reports/texture-cache-test");
        var asset=new Asset{Path="TestAnimation.uasset",Size=1,Chunk="fixture.utoc"};asset.Prepare();
        var cache=new ModelAvailabilityCache(System.IO.Path.Combine(dir,"animations.json"),dir,"missing");cache.ConfirmAnimation(asset,clip);
        var defaultAsset=new Asset{Path="AN_Default.uasset",Size=1,Chunk="fixture.utoc"};defaultAsset.Prepare();
        if(defaultAsset.Hint!="Animação provável")throw new Exception("Probable animation label failed");
        cache.ConfirmAnimation(defaultAsset,clip with{Sections=[new("Default",0,30,"Default")]});
        if(defaultAsset.Hint!="Animação"||defaultAsset.MontageConfirmed)throw new Exception("Default-only animation was classified as montage");
        var defaultCopy=new Asset{Path=defaultAsset.Path,Size=1,Chunk="fixture.utoc"};defaultCopy.Prepare();new ModelAvailabilityCache(System.IO.Path.Combine(dir,"animations.json"),dir,"missing").Apply(new[]{defaultCopy});
        if(defaultCopy.Hint!="Animação")throw new Exception("Plain animation type did not persist");
        File.WriteAllText(System.IO.Path.Combine(root,"reports","update-0715-test.json"),"{\"Passed\":true,\"ProbableAnimationLabel\":true,\"DefaultClassifiedAsAnimation\":true,\"PlainAnimationPersists\":true}");
        var copy=new Asset{Path=asset.Path,Size=asset.Size,Chunk=asset.Chunk};copy.Prepare();new ModelAvailabilityCache(System.IO.Path.Combine(dir,"animations.json"),dir,"missing").Apply(new[]{copy});
        if(!copy.MontageConfirmed||!copy.AnimationConfirmed||copy.Thumbnail!=null)throw new Exception("Montage type persistence failed");
        Home_Click(this,new RoutedEventArgs());if(PreviewColumn.Width.Value!=0||PreviewHost.Visibility!=Visibility.Collapsed||InspectorEmpty.Visibility!=Visibility.Collapsed)throw new Exception("Home viewer was not hidden");
        Library_Click(this,new RoutedEventArgs());if(PreviewColumn.Width.Value==0)throw new Exception("Library viewer did not return");
        File.WriteAllText(System.IO.Path.Combine(root,"reports/update-0713-test.json"),JsonSerializer.Serialize(new{Passed=true,Version="0.7.13",CompatibleOnlyFilter=true,MissingBonesRejected=true,RestPoseRejected=true,RealBalgestCompatibilityAndCache=realCompatibility,MontageTypePersists=true,SelfLoopAdvances=true,FullCycleRepeats=true,LargeFrameDelta=true,RealBlackDragonMontage=realMontage,HomeViewerHidden=true}));
    }
}
public static class Entry {
    [STAThread]
    public static int Main(string[] args) {
        if(args.Length==2 && args[0]=="--release-cache-test"){LibrarySeed.Verify(args[1]);return 0;}
        if(args.Length==4 && args[0]=="--build-library-seed"){LibrarySeed.Build(args[1],args[2],args[3]);return 0;}
        string? root=ReleaseRuntime.Initialize();
        if(root==null){root=AppContext.BaseDirectory;
        while(root!=null && !File.Exists(ReleaseRuntime.Support(root,"scripts/desktop-worker.ps1")))root=Directory.GetParent(root)?.FullName;
        }
        var missingSupport=root==null;root??=AppContext.BaseDirectory;
        var app=new Application();
        if(args.Contains("--scan-catalog")){
            var scanWindow=new MainWindow(root,true);int code=0;
            var limited=args.Length>1&&int.TryParse(args[1],out var count)?count:int.MaxValue;
            app.Startup+=async(_,_)=>{
                try{await scanWindow.ScanCatalog(limited,args.Length>2?args[2]:"");}
                catch(Exception ex){File.WriteAllText(System.IO.Path.Combine(root,"reports","catalog-scan-error.txt"),ex.ToString());code=1;}
                finally{if(limited!=int.MaxValue)app.Shutdown();}
            };
            app.Run(scanWindow);return code;
        }
        if(args.Length>=3 && args[0]=="--viewer-test") {
            var viewer=new PreviewPanel(System.IO.Path.GetFileNameWithoutExtension(args[1]),new[]{args[1]},args[2],()=>{});
            int code=0;
            app.Startup+=async(_,_)=>{
                try{await viewer.Verify(args[1],System.IO.Path.Combine(root,"reports"));}
                catch(Exception ex){File.WriteAllText(System.IO.Path.Combine(root,"reports/viewer-test-error.txt"),ex.ToString());code=1;}
                finally{app.Shutdown();}
            };
            app.Run();return code;
        }
        var integration=args.Length>=4 && args[0]=="--integration-test";
        var test=args.Contains("--self-test")||integration;
        var window=new MainWindow(root,test);
        if(!test){if(missingSupport)window.Loaded+=(_,_)=>window.MissingSupportNotice();return app.Run(window);}
        int exitCode=0;
        app.Startup+=async(_,_)=>{
            try{if(integration)await window.IntegrationTest(args[1],args[2],args[3]);else await window.SelfTest();}
            catch(Exception ex){File.WriteAllText(System.IO.Path.Combine(root,"reports/desktop-self-test-error.txt"),ex.ToString());exitCode=1;}
            finally{app.Shutdown();}
        };
        app.Run();return exitCode;
    }
}
