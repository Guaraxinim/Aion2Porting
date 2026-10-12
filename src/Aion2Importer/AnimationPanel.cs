using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
namespace Aion2Importer;
public sealed class AnimationPanel : UserControl,IDisposable {
    readonly AnimationClipData clip;
    readonly Viewport3D viewport=new();
    readonly PerspectiveCamera camera=new(){FieldOfView=45,UpDirection=new(0,1,0),NearPlaneDistance=.001,FarPlaneDistance=1000};
    readonly ModelVisual3D visual=new();
    readonly MeshGeometry3D bones=new();
    readonly Slider seek=new(){Minimum=0};
    readonly TextBlock status=new(){Foreground=Brushes.LightGray,TextWrapping=TextWrapping.Wrap};
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(1000d/30)};
    readonly Stopwatch clock=new();
    AnimatedMesh? mesh;
    string? modelPath;
    readonly Button combinedQueue;
    readonly Button textureButton=new(){Content="Textura base",IsEnabled=false};
    readonly CheckBox compatibleOnly=new(){Content="Somente compatíveis com este esqueleto",ToolTip="Compara ossos e pose base salvos no cache. Modelos ainda não verificados ficam ocultos."};
    readonly TextBox modelSearch=new(){Margin=new Thickness(0,6,0,6)};
    readonly ListBox modelList=new(){Height=140,DisplayMemberPath="Path",Background=new SolidColorBrush(Color.FromRgb(8,10,18)),Foreground=Brushes.LightGray,BorderBrush=new SolidColorBrush(Color.FromRgb(42,50,78))};
    readonly TextBlock modelCount=new(){Foreground=Brushes.LightGray,TextWrapping=TextWrapping.Wrap};
    readonly ComboBox sectionChoice=new(){DisplayMemberPath="Name"};
    readonly ComboBox variantChoice=new(){DisplayMemberPath="Name"};
    readonly StackPanel selectors=new();
    readonly CheckBox followSections=new(){Content="Seguir seções da montagem",IsChecked=true};
    int elapsedFrames;
    public event Action<string>? VariantRequested;
    public string? CurrentSection=>SectionAt(Frame)?.Name;
    public bool HasSectionSelector=>clip.Sections is {Length:>0} sections&&!(sections.Length==1&&string.Equals(sections[0].Name,"Default",StringComparison.OrdinalIgnoreCase));
    Asset[] library=[];
    string? assetRoot;
    public event Action? InspectRequested;
    public event Action<Asset>? LibraryModelRequested;
    public event Action<string>? NoticeRequested;
    bool updating;int startFrame;
    double yaw=.4,pitch=.1,distance=4;Point3D center;Point last;
    public int Frame {get;private set;}
    public bool IsPlaying=>timer.IsEnabled;
    public bool HasModel=>mesh!=null;
    public event Action? CloseRequested;
    public event Action<string?>? QueueRequested;
    public event Action<string>? Failed;
    public AnimationPanel(AnimationClipData clip,string? previousModel=null) {
        this.clip=clip;
        Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/Aion2Porting;component/Design.xaml",UriKind.Relative)});
        Background=new SolidColorBrush(Color.FromRgb(18,22,39));
        var grid=new Grid{Margin=new Thickness(12)};
        foreach(var height in new[]{GridLength.Auto,GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto})grid.RowDefinitions.Add(new(){Height=height});
        void Add(UIElement e,int row){Grid.SetRow(e,row);grid.Children.Add(e);}
        var header=new DockPanel();var info=new Button{Content="ⓘ",ToolTip="Inspecionar asset"};DockPanel.SetDock(info,Dock.Right);info.Click+=(_,_)=>InspectRequested?.Invoke();header.Children.Add(info);
        header.Children.Add(new TextBlock{Text=clip.Name+"\nEsqueleto: "+clip.Skeleton,Foreground=Brushes.Cyan,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,10)});Add(header,0);
        var actions=new WrapPanel();
        void Button(string caption,Action action){var b=new Button{Content=caption,Padding=new Thickness(8)};b.Click+=(_,_)=>action();actions.Children.Add(b);}
        Button("▶ Reproduzir",Play);Button("Ⅱ Pausar",Pause);Button("■ Parar",()=>{Pause();ShowFrame(0);});Button("Fechar",()=>CloseRequested?.Invoke());Add(actions,1);
        if(previousModel!=null&&System.IO.File.Exists(previousModel))Button("Usar modelo visualizado",()=>{try{LoadModel(previousModel);}catch(Exception ex){Failed?.Invoke(ex.Message);}});
        Button("Fila: esqueleto + animação",()=>{Pause();QueueRequested?.Invoke(null);});
        combinedQueue=new Button{Content="Fila: modelo + texturas + animação",Padding=new Thickness(8),IsEnabled=false,ToolTip="Carregue ou selecione um GLB compatível primeiro"};
        combinedQueue.Click+=(_,_)=>{Pause();QueueRequested?.Invoke(modelPath);};actions.Children.Add(combinedQueue);
        var picker=new StackPanel();picker.Children.Add(compatibleOnly);compatibleOnly.Checked+=(_,_)=>RefreshModels();compatibleOnly.Unchecked+=(_,_)=>RefreshModels();picker.Children.Add(modelSearch);picker.Children.Add(modelCount);picker.Children.Add(modelList);
        ScrollViewer.SetHorizontalScrollBarVisibility(modelList,ScrollBarVisibility.Disabled);
        modelSearch.ToolTip="Buscar entre modelos prováveis e disponíveis";
        modelSearch.TextChanged+=(_,_)=>RefreshModels();
        var apply=new Button{Content="Aplicar modelo selecionado"};apply.Click+=(_,_)=>{if(modelList.SelectedItem is Asset asset)LibraryModelRequested?.Invoke(asset);};picker.Children.Add(apply);
        modelList.MouseDoubleClick+=(_,_)=>{if(modelList.SelectedItem is Asset asset)LibraryModelRequested?.Invoke(asset);};
        Add(selectors,2);
        if(HasSectionSelector){
            selectors.Children.Add(new TextBlock{Text="Seção da montagem",Foreground=Brushes.LightGray,Margin=new Thickness(0,6,0,4)});
            sectionChoice.ItemsSource=clip.Sections;sectionChoice.SelectionChanged+=(_,_)=>{if(!updating&&sectionChoice.SelectedItem is AnimationSection section){Pause();ShowFrame(section.StartFrame);}};
            selectors.Children.Add(sectionChoice);selectors.Children.Add(followSections);
            followSections.Checked+=(_,_)=>{if(IsPlaying)Play();};followSections.Unchecked+=(_,_)=>{if(IsPlaying)Play();};
        }
        selectors.Children.Add(new Expander{Header="Escolher modelo da biblioteca",Foreground=Brushes.Cyan,Content=picker,Margin=new Thickness(0,8,0,8)});
        textureButton.Click+=(_,_)=>{try{var count=mesh?.ApplyTextures(assetRoot??System.IO.Path.GetDirectoryName(modelPath!)!)??0;UpdateStatus();if(count==0)Failed?.Invoke("Nenhuma textura base disponível para este modelo.");else NoticeRequested?.Invoke($"Textura aplicada em {count} partes do modelo.");}catch(Exception ex){Failed?.Invoke(ex.Message);}};actions.Children.Add(textureButton);
        viewport.Camera=camera;
        var lights=new Model3DGroup();lights.Children.Add(new AmbientLight(Colors.Gray));lights.Children.Add(new DirectionalLight(Colors.White,new(-1,-1,-2)));
        viewport.Children.Add(new ModelVisual3D{Content=lights});
        var material=new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(79,172,254)));material.Freeze();
        visual.Content=new GeometryModel3D(bones,material){BackMaterial=material};viewport.Children.Add(visual);
        var surface=new Border{Background=new SolidColorBrush(Color.FromRgb(8,10,18)),Child=viewport,MinHeight=160};
        surface.MouseDown+=(_,e)=>{if(e.ChangedButton==MouseButton.Left){last=e.GetPosition(surface);surface.CaptureMouse();}};
        surface.MouseMove+=(_,e)=>{if(surface.IsMouseCaptured){var point=e.GetPosition(surface);yaw-=(point.X-last.X)*.008;pitch=Math.Clamp(pitch+(point.Y-last.Y)*.008,-1.4,1.4);last=point;Camera();}};
        surface.MouseUp+=(_,_)=>surface.ReleaseMouseCapture();
        surface.MouseWheel+=(_,e)=>{distance=Math.Clamp(distance*Math.Exp(-e.Delta*.001),.05,100);Camera();};
        surface.SizeChanged+=(_,_)=>Fit();Add(surface,3);
        seek.Maximum=clip.Frames.Length-1;seek.Margin=new Thickness(0,12,0,8);
        seek.ValueChanged+=(_,_)=>{if(!updating){Pause();ShowFrame((int)seek.Value);}};Add(seek,4);Add(status,5);
        Content=grid;
        timer.Tick+=(_,_)=>Tick();
        ShowFrame(0);Fit();
    }
    public void SetLibrary(IEnumerable<Asset> assets){library=assets.Where(a=>a.Hint=="Modelo provável"||a.Hint==ModelAvailabilityCache.AvailableHint).ToArray();RefreshModels();}
    public void SetVariants(AnimationVariant[] variants,string currentFile){
        if(variants.Length<2)return;
        variantChoice.ItemsSource=variants;variantChoice.SelectedItem=variants.FirstOrDefault(v=>v.File==currentFile);
        variantChoice.SelectionChanged+=(_,_)=>{if(variantChoice.SelectedItem is AnimationVariant variant&&variant.File!=currentFile){Pause();VariantRequested?.Invoke(variant.File);}};
        selectors.Children.Insert(0,variantChoice);selectors.Children.Insert(0,new TextBlock{Text="Trilha da montagem",Foreground=Brushes.LightGray});
    }
    AnimationSection? SectionAt(int frame)=>clip.Sections?.LastOrDefault(s=>frame>=s.StartFrame&&frame<s.EndFrame);
    public void SelectSection(string name){var section=clip.Sections?.FirstOrDefault(s=>s.Name==name)??throw new ArgumentException("Seção não encontrada.");Pause();ShowFrame(section.StartFrame);}
    void Tick(){
        int target=(int)(clock.Elapsed.TotalSeconds*clip.Fps),delta=target-elapsedFrames;elapsedFrames=target;
        if(clip.Sections is not {Length:>0}||followSections.IsChecked!=true){ShowFrame((startFrame+target)%clip.Frames.Length);return;}
        AdvanceMontage(delta);
    }
    public void AdvanceMontage(int delta){
        var sections=clip.Sections?.OrderBy(s=>s.StartFrame).ToArray()??[];
        if(sections.Length==0){ShowFrame((int)(((long)Frame+Math.Max(0,delta))%clip.Frames.Length));return;}
        var spans=sections.Select((s,i)=>(Start:s.StartFrame,End:i+1<sections.Length?Math.Min(s.EndFrame,sections[i+1].StartFrame):s.EndFrame)).Where(s=>s.End>s.Start).ToArray();
        int cycle=spans.Sum(s=>s.End-s.Start),offset=0;
        if(cycle==0)return;
        foreach(var span in spans){if(Frame>=span.Start&&Frame<span.End){offset+=Frame-span.Start;break;}offset+=span.End-span.Start;}
        var target=(int)(((long)offset+Math.Max(0,delta))%cycle);
        foreach(var span in spans){if(target<span.End-span.Start){ShowFrame(span.Start+target);return;}target-=span.End-span.Start;}
    }
    void RefreshModels(){var terms=modelSearch.Text.Split(' ',StringSplitOptions.RemoveEmptyEntries);var matches=library.Where(a=>(compatibleOnly.IsChecked!=true||a.SkeletonProfile?.Matches(clip)==true)&&terms.All(t=>a.Path.Contains(t,StringComparison.OrdinalIgnoreCase))).ToArray();modelList.ItemsSource=matches.Take(100).ToArray();modelCount.Text=$"{matches.Length:N0} modelos • até 100 exibidos. Refine a busca.";}
    public void LoadModel(string path,string? root=null) {
        if(!SkeletonProfile.Read(path).Matches(clip))throw new InvalidDataException("Pose base ou ossos incompatíveis com esta animação.");
        var candidate=new AnimatedMesh(path,clip);
        candidate.Update(clip.World(clip.Frames[Frame]));mesh=candidate;modelPath=path;assetRoot=root;textureButton.IsEnabled=true;combinedQueue.IsEnabled=true;combinedQueue.ToolTip=path;visual.Content=mesh.Scene;UpdateStatus();Fit();
    }
    void UpdateStatus()=>status.Text=$"Quadro {Frame+1}/{clip.Frames.Length} • {clip.Duration:0.00}s\n"+(mesh==null?"Prévia do esqueleto. Escolha um modelo compatível da biblioteca.":$"Malha animada • {mesh.Vertices:N0} vértices • {(mesh.TexturedParts>0?"textura base":"material neutro")}")+
        "\nArraste para girar • Roda para zoom • Sem física ou retargeting automático"+(clip.Kind=="Montage"?"\nMontagem: trechos e seções. Eventos, física e mistura entre trilhas não simulados.":"");
    public void ShowFrame(int frame) {
        var watch=Stopwatch.StartNew();
        Frame=Math.Clamp(frame,0,clip.Frames.Length-1);
        var world=clip.World(clip.Frames[Frame]);
        if(mesh!=null)mesh.Update(world);
        else {
            var positions=new Point3DCollection();var indices=new Int32Collection();
            for(int i=0;i<world.Length;i++) {
                var parent=clip.Bones[i].Parent;if(parent<0)continue;
                var a=world[parent].Translation;var b=world[i].Translation;var d=b-a;
                if(d.LengthSquared()<1e-10f)continue;
                var u=System.Numerics.Vector3.Cross(System.Numerics.Vector3.Normalize(d),Math.Abs(d.Y)<d.Length()*.9?System.Numerics.Vector3.UnitY:System.Numerics.Vector3.UnitX);
                u=System.Numerics.Vector3.Normalize(u)*.006f;
                var v=System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(d,u))*.006f;int offset=positions.Count;
                foreach(var point in new[]{a-u-v,a+u-v,a+u+v,a-u+v,b-u-v,b+u-v,b+u+v,b-u+v})positions.Add(new(point.X,point.Y,point.Z));
                foreach(var index in new[]{0,1,5,0,5,4,1,2,6,1,6,5,2,3,7,2,7,6,3,0,4,3,4,7})indices.Add(offset+index);
            }
            bones.Positions=positions;bones.TriangleIndices=indices;
        }
        updating=true;seek.Value=Frame;if(clip.Sections is {Length:>0})sectionChoice.SelectedItem=SectionAt(Frame);updating=false;UpdateStatus();
        if(watch.ElapsedMilliseconds>40)timer.Interval=TimeSpan.FromMilliseconds(80);
    }
    void Fit(){
        var b=visual.Content?.Bounds??Rect3D.Empty;if(b.IsEmpty)return;
        center=new(b.X+b.SizeX/2,b.Y+b.SizeY/2,b.Z+b.SizeZ/2);
        distance=Math.Max(.1,Math.Sqrt(b.SizeX*b.SizeX+b.SizeY*b.SizeY+b.SizeZ*b.SizeZ)*2.2);
        Camera();
    }
    void Camera(){var offset=new Vector3D(Math.Sin(yaw)*Math.Cos(pitch),Math.Sin(pitch),Math.Cos(yaw)*Math.Cos(pitch))*distance;camera.Position=center+offset;camera.LookDirection=-offset;}
    public void Play(){startFrame=Frame;elapsedFrames=0;clock.Restart();timer.Start();}
    public void Pause(){timer.Stop();clock.Stop();}
    public void Dispose(){Pause();viewport.Children.Clear();}
}
