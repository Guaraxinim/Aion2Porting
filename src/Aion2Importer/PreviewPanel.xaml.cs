using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace Aion2Importer;
public partial class PreviewPanel : System.Windows.Controls.UserControl {
    readonly string assetRoot;
    readonly Action enqueue;
    readonly int exportFailures;
    PreviewScene? scene;
    int version;
    Point previous;
    bool pan;
    Point3D target;
    double yaw=.45,pitch=.15,distance=3,radius=1;
    bool closed,verifying;
    public PreviewPanel(string name,string[] models,string assetRoot,Action enqueue,int failures=0) {
        this.assetRoot=assetRoot;this.enqueue=enqueue;exportFailures=failures;
        InitializeComponent();AssetTitle.Text=name;
        Models.Visibility=models.Length>1?Visibility.Visible:Visibility.Collapsed;
        Models.ItemsSource=models.Select(p=>new ModelChoice(p,Path.GetFileNameWithoutExtension(p))).ToArray();


        KeyDown+=(_,e)=>{if(e.Key==Key.F)Fit();if(e.Key==Key.Escape)ClosePanel();};
    }
    public void HideQueueAction()=>AddQueue.Visibility=Visibility.Collapsed;
    public event Action<PreviewScene>? ModelLoaded;
    public event Action? CloseRequested;
    public event Action? InspectRequested;
    void Inspect_Click(object sender,RoutedEventArgs e)=>InspectRequested?.Invoke();
    public event Action<string>? ModelFailed;
    public void DisposePreview() {closed=true;version++;Surface.ReleaseMouseCapture();ObjectVisual.Content=null;scene=null;advancedMaterialScene=null;advancedEnabled=false;currentModelPath=null;}
    void ClosePanel()=>CloseRequested?.Invoke();
    public async Task OpenAsync() {
        verifying=true;Models.SelectedIndex=0;verifying=false;
        if(Models.SelectedItem is ModelChoice model)await LoadModel(model.Path);
    }
    sealed record ModelChoice(string Path,string Name);
    async void ModelChanged(object sender,System.Windows.Controls.SelectionChangedEventArgs e) {
        if(verifying || Models.SelectedItem is not ModelChoice choice)return;
        var selectedVersion=version+1;
        try {await LoadModel(choice.Path);}
        catch(Exception ex) {if(!closed && selectedVersion==version){ModelFailed?.Invoke("Não foi possível mostrar este modelo: "+ex.Message);LoadingText.Text="Não foi possível mostrar este modelo.\n"+ex.Message;Loading.Visibility=Visibility.Visible;}}
    }
    async Task LoadModel(string path) {
        var current=++version;currentModelPath=path;advancedMaterialScene=null;advancedEnabled=false;AdvancedMaterials.Content="Ativar materiais avançados";AdvancedMaterials.IsEnabled=true;Textures.IsEnabled=true;
        LoadingText.Text="Carregando geometria…";Loading.Visibility=Visibility.Visible;AddQueue.IsEnabled=false;
        ObjectVisual.Content=null;scene=null;Stats.Text="";
        var loaded=await Task.Run(()=>PreviewLoader.Load(path,assetRoot));
        if(current!=version||closed)return;
        scene=loaded;ObjectVisual.Content=Textures.IsChecked==true?scene.Textured:scene.Solid;
        Stats.Text=$"{scene.Vertices:N0} vértices • {scene.Triangles:N0} triângulos • {scene.Parts} partes • {scene.TexturedParts} com textura";
        Notice.Text="Pose de referência • Cor base opaca • Shaders e transparência do jogo não reproduzidos";
        if(scene.Warnings.Length>0)Notice.Text+="\n"+string.Join(" ",scene.Warnings);
        if(exportFailures>0)Notice.Text+="\nA conversão reportou falhas auxiliares; a prévia pode estar incompleta.";
        Fit();Loading.Visibility=Visibility.Collapsed;AddQueue.IsEnabled=true;
        ModelLoaded?.Invoke(scene);
    }
    Model3DGroup? advancedMaterialScene;
    string? currentModelPath;
    bool advancedEnabled;
    async void AdvancedMaterials_Click(object sender,RoutedEventArgs e) {
        try{await ToggleAdvancedMaterials();}
        catch(Exception ex){ModelFailed?.Invoke("Materiais avançados indisponíveis: "+ex.Message);}
    }
    public async Task ToggleAdvancedMaterials() {
        if(scene==null||currentModelPath==null)return;
        if(advancedEnabled) {
            advancedEnabled=false;advancedMaterialScene=null;
            ObjectVisual.Content=Textures.IsChecked==true?scene.Textured:scene.Solid;
            AdvancedMaterials.Content="Ativar materiais avançados";Textures.IsEnabled=true;
            Notice.Text="Modo básico • Pose de referência • Shaders do jogo não reproduzidos";return;
        }
        var expectedVersion=version;var baseline=scene;var path=currentModelPath;
        AdvancedMaterials.IsEnabled=false;
        try {
            var materials=await Task.Run(()=>PreviewLoader.LoadAdvancedMaterials(baseline,path,assetRoot));
            if(closed||version!=expectedVersion)return;
            advancedMaterialScene=materials;advancedEnabled=true;ObjectVisual.Content=materials;Textures.IsEnabled=false;
            AdvancedMaterials.Content="Desativar materiais avançados";
            Notice.Text="Materiais aproximados • Textura, alpha e brilho simples • Não executa shaders Unreal; transparência pode apresentar diferenças.";
        }finally{if(!closed&&version==expectedVersion)AdvancedMaterials.IsEnabled=true;}
    }
    public string? CurrentModelPath=>currentModelPath;
    public bool AdvancedMaterialsActive=>advancedEnabled;

    public byte[] CaptureThumbnail() {
        if(scene==null)throw new InvalidOperationException("Modelo não carregado.");
        const int width=192,height=144;
        var viewport=new System.Windows.Controls.Viewport3D();
        var camera=Camera.Clone();
        var b=scene.Bounds;
        var center=new Point3D(b.X+b.SizeX/2,b.Y+b.SizeY/2,b.Z+b.SizeZ/2);
        var r=Math.Max(Math.Sqrt(b.SizeX*b.SizeX+b.SizeY*b.SizeY+b.SizeZ*b.SizeZ)/2,1e-6);
        var angle=Math.Atan(Math.Tan(camera.FieldOfView*Math.PI/360)/(width/(double)height));
        var d=r/Math.Sin(angle)*1.12;
        var offset=new Vector3D(Math.Sin(.45)*Math.Cos(.15),Math.Sin(.15),Math.Cos(.45)*Math.Cos(.15))*d;
        camera.Position=center+offset;camera.LookDirection=-offset;
        camera.NearPlaneDistance=Math.Max(r*.0001,d-r*1.8);camera.FarPlaneDistance=d+r*10;
        viewport.Camera=camera;
        foreach(var child in Viewport.Children.OfType<ModelVisual3D>())
            viewport.Children.Add(new ModelVisual3D{Content=child.Content});
        var surface=new System.Windows.Controls.Grid{Background=new SolidColorBrush(Color.FromRgb(13,16,29))};
        surface.Children.Add(viewport);surface.Measure(new Size(width,height));surface.Arrange(new Rect(0,0,width,height));surface.UpdateLayout();
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(surface);
        var encoder=new JpegBitmapEncoder{QualityLevel=72};encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output=new MemoryStream();encoder.Save(output);return output.ToArray();
    }
    void TextureChanged(object sender,RoutedEventArgs e) {
        if(scene!=null&&!advancedEnabled)ObjectVisual.Content=Textures.IsChecked==true?scene.Textured:scene.Solid;
    }
    void Fit() {
        if(scene==null)return;
        var b=scene.Bounds;target=new(b.X+b.SizeX/2,b.Y+b.SizeY/2,b.Z+b.SizeZ/2);
        radius=Math.Max(Math.Sqrt(b.SizeX*b.SizeX+b.SizeY*b.SizeY+b.SizeZ*b.SizeZ)/2,1e-6);
        var aspect=Math.Max(Surface.ActualWidth,1)/Math.Max(Surface.ActualHeight,1);
        var halfAngle=Math.Atan(Math.Tan(Camera.FieldOfView*Math.PI/360)/Math.Max(1,aspect));
        distance=radius/Math.Sin(halfAngle)*1.12;yaw=.45;pitch=.15;UpdateCamera();
    }
    void UpdateCamera() {
        var offset=new Vector3D(Math.Sin(yaw)*Math.Cos(pitch),Math.Sin(pitch),Math.Cos(yaw)*Math.Cos(pitch))*distance;
        Camera.Position=target+offset;Camera.LookDirection=-offset;
        Camera.NearPlaneDistance=Math.Max(radius*.0001,distance-radius*1.8);
        Camera.FarPlaneDistance=Math.Max(distance+radius*10,Camera.NearPlaneDistance*100);
    }
    void Orbit(double x,double y) {yaw-=x*.008;pitch=Math.Clamp(pitch+y*.008,-1.5,1.5);UpdateCamera();}
    void Zoom(int delta) {distance=Math.Clamp(distance*Math.Exp(-delta*.001),radius*.12,radius*100);UpdateCamera();}
    void Pan(double x,double y) {
        var forward=Camera.LookDirection;forward.Normalize();
        var right=Vector3D.CrossProduct(forward,new Vector3D(0,1,0));right.Normalize();
        var up=Vector3D.CrossProduct(right,forward);up.Normalize();
        var scale=2*distance*Math.Tan(Camera.FieldOfView*Math.PI/360)/Math.Max(Surface.ActualWidth,1);
        target+=(-right*x+up*y)*scale;UpdateCamera();
    }
    void PointerDown(object sender,MouseButtonEventArgs e) {
        if(scene==null)return;
        if(e.ChangedButton is not (MouseButton.Left or MouseButton.Right or MouseButton.Middle))return;
        Focus();previous=e.GetPosition(Surface);pan=e.ChangedButton!=MouseButton.Left;Surface.CaptureMouse();e.Handled=true;
    }
    void PointerMove(object sender,MouseEventArgs e) {
        if(!Surface.IsMouseCaptured||scene==null)return;
        var p=e.GetPosition(Surface);var d=p-previous;previous=p;
        if(pan)Pan(d.X,d.Y);else Orbit(d.X,d.Y);
    }
    void PointerUp(object sender,MouseButtonEventArgs e) {Surface.ReleaseMouseCapture();}
    void Wheel(object sender,MouseWheelEventArgs e) {if(scene!=null){Zoom(e.Delta);e.Handled=true;}}
    void Fit_Click(object sender,RoutedEventArgs e)=>Fit();
    void Front_Click(object sender,RoutedEventArgs e) {yaw=0;pitch=0;UpdateCamera();}
    void Side_Click(object sender,RoutedEventArgs e) {yaw=Math.PI/2;pitch=0;UpdateCamera();}
    void SurfaceSizeChanged(object sender,SizeChangedEventArgs e) {if(scene!=null)Fit();}
    void Back_Click(object sender,RoutedEventArgs e)=>ClosePanel();
    void Add_Click(object sender,RoutedEventArgs e) {enqueue();AddQueue.Content="Adicionado à fila";AddQueue.IsEnabled=false;}
    public async Task Verify(string path,string reportRoot) {
        // Run through the actual renderer and camera operations, with a real exported asset.
        var host=new Window {Width=1040,Height=800,Content=this,ShowActivated=false,ShowInTaskbar=false,Left=-10000,Top=-10000};
        verifying=true;Models.SelectedIndex=0;host.Show();
        await LoadModel(path);
        if(scene==null||scene.Triangles==0)throw new Exception("Empty preview");
        var original=Camera.Position;Orbit(50,20);
        if(Camera.Position==original)throw new Exception("Orbit did not move camera");
        var before=distance;Zoom(120);if(distance>=before)throw new Exception("Zoom failed");
        var center=target;Pan(40,20);if(center==target)throw new Exception("Pan failed");
        Textures.IsChecked=false;if(ObjectVisual.Content!=scene.Solid)throw new Exception("Solid mode failed");
        Textures.IsChecked=true;if(ObjectVisual.Content!=scene.Textured)throw new Exception("Texture mode failed");
        Textures.IsChecked=false;Fit();
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        UpdateLayout();
        var visual=(FrameworkElement)host;
        visual.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)visual.ActualWidth,(int)visual.ActualHeight,96,96,PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using(var file=File.Create(Path.Combine(reportRoot,"viewer-preview.png")))encoder.Save(file);
        File.WriteAllText(Path.Combine(reportRoot,"viewer-test.json"),JsonSerializer.Serialize(new{Passed=true,scene.Vertices,scene.Triangles,scene.Parts,scene.TexturedParts,CameraControls=true,SolidToggle=true,scene.Warnings}));
    }
}
