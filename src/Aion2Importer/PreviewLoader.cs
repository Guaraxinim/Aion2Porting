using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using SharpGLTF.Schema2;

namespace Aion2Importer;

// Reads the reference pose; animation, game shaders and alpha sorting are deliberately not approximated.
public sealed record PreviewScene(Model3DGroup Textured,Model3DGroup Solid,int Vertices,int Triangles,int TexturedParts,int Parts,Rect3D Bounds,string[] Warnings);
public static class PreviewLoader {
    public static PreviewScene Load(string path,string assetRoot) {
        if(new FileInfo(path).Length>256L*1024*1024)throw new InvalidDataException("Modelo acima do limite de 256 MB da prévia.");
        var model=ModelRoot.Load(path);
        var textured=new Model3DGroup();var solid=new Model3DGroup();
        var vertices=0;var triangles=0;var texturedParts=0;var parts=0;
        var warnings=new HashSet<string>();
        var metadata=Directory.EnumerateFiles(assetRoot,"*.json",SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension,StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g=>g.Key!,g=>g.ToArray(),StringComparer.OrdinalIgnoreCase);
        IEnumerable<Node> Walk(Node node) {yield return node;foreach(var child in node.VisualChildren)foreach(var n in Walk(child))yield return n;}
        var scene=model.DefaultScene??model.LogicalScenes.FirstOrDefault()??throw new InvalidDataException("O GLB não contém uma cena.");
        foreach(var node in scene.VisualChildren.SelectMany(Walk)) {
            if(node.Mesh==null)continue;
            var matrix=node.WorldMatrix;
            if(!Matrix4x4.Invert(matrix,out var inverse))throw new InvalidDataException("Transformação inválida.");
            var normalMatrix=Matrix4x4.Transpose(inverse);
            foreach(var primitive in node.Mesh.Primitives) {
                var positions=primitive.GetVertexAccessor("POSITION")?.AsVector3Array();
                if(positions==null||positions.Count==0)continue;
                var indices=primitive.GetTriangleIndices().ToArray();
                if(indices.Length==0)continue;
                triangles+=indices.Length;vertices+=positions.Count;
                if(triangles>2_000_000||vertices>4_000_000)throw new InvalidDataException("Modelo muito grande para esta prévia (limite de 2 milhões de triângulos).");
                var geometry=new MeshGeometry3D();
                foreach(var p in positions) {
                    var point=Vector3.Transform(p,matrix);
                    if(!float.IsFinite(point.X)||!float.IsFinite(point.Y)||!float.IsFinite(point.Z))throw new InvalidDataException("Coordenadas inválidas na malha.");
                    geometry.Positions.Add(new Point3D(point.X,point.Y,point.Z));
                }
                var flipped=matrix.GetDeterminant()<0;
                foreach(var (a,b,c) in indices) {
                    if(a<0||b<0||c<0||a>=positions.Count||b>=positions.Count||c>=positions.Count)throw new InvalidDataException("Índice de vértice inválido.");
                    geometry.TriangleIndices.Add(a);geometry.TriangleIndices.Add(flipped?c:b);geometry.TriangleIndices.Add(flipped?b:c);
                }
                var normals=primitive.GetVertexAccessor("NORMAL")?.AsVector3Array();
                if(normals?.Count==positions.Count)foreach(var n in normals) {
                    var normal=Vector3.TransformNormal(n,normalMatrix);
                    if(normal.LengthSquared()>1e-12f)normal=Vector3.Normalize(normal);
                    geometry.Normals.Add(new Vector3D(normal.X,normal.Y,normal.Z));
                }
                var uv=primitive.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();
                if(uv?.Count==positions.Count)foreach(var t in uv)geometry.TextureCoordinates.Add(new Point(t.X,t.Y));
                geometry.Freeze();
                var plain=new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(154,181,199)));plain.Freeze();
                System.Windows.Media.Media3D.Material material=plain;
                if(uv?.Count==positions.Count) {
                    try {
                        var bitmap=ReadTexture(primitive.Material,assetRoot,metadata);
                        if(bitmap!=null) {
                            // Opaque base color makes hair cards inspectable without claiming alpha/shader fidelity.
                            var opaque=new FormatConvertedBitmap(bitmap,PixelFormats.Bgr32,null,0);opaque.Freeze();
                            var brush=new ImageBrush(opaque){ViewportUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,1,1),TileMode=TileMode.Tile,Stretch=Stretch.Fill};
                            brush.Freeze();material=new DiffuseMaterial(brush);material.Freeze();texturedParts++;
                        }
                    }catch(Exception ex) {warnings.Add("Textura indisponível: "+ex.Message);}
                }
                textured.Children.Add(new GeometryModel3D(geometry,material){BackMaterial=material});
                solid.Children.Add(new GeometryModel3D(geometry,plain){BackMaterial=plain});
                parts++;
            }
        }
        if(parts==0)throw new InvalidDataException("Este arquivo não contém triângulos para visualizar.");
        if(texturedParts<parts)warnings.Add("Algumas partes usam cor neutra por falta de textura base.");
        textured.Freeze();solid.Freeze();
        var bounds=textured.Bounds;
        if(bounds.IsEmpty||Math.Max(bounds.SizeX,Math.Max(bounds.SizeY,bounds.SizeZ))<=1e-9)throw new InvalidDataException("A malha não possui dimensões visíveis.");
        return new(textured,solid,vertices,triangles,texturedParts,parts,bounds,warnings.ToArray());
    }
    public static Model3DGroup LoadAdvancedMaterials(PreviewScene baseline,string path,string assetRoot) {
        if(new FileInfo(path).Length>256L*1024*1024)throw new InvalidDataException("Modelo acima do limite da prévia.");
        var model=ModelRoot.Load(path);
        var metadata=Directory.EnumerateFiles(assetRoot,"*.json",SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension,StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g=>g.Key!,g=>g.ToArray(),StringComparer.OrdinalIgnoreCase);
        IEnumerable<Node> Walk(Node n){yield return n;foreach(var c in n.VisualChildren)foreach(var child in Walk(c))yield return child;}
        var source=model.DefaultScene??model.LogicalScenes.First();
        var group=new Model3DGroup();int part=0;
        foreach(var node in source.VisualChildren.SelectMany(Walk)) {
            if(node.Mesh==null)continue;
            foreach(var primitive in node.Mesh.Primitives) {
                if(primitive.GetVertexAccessor("POSITION")?.Count is not >0||!primitive.GetTriangleIndices().Any())continue;
                if(part>=baseline.Textured.Children.Count)throw new InvalidDataException("Partes do modelo incompatíveis.");
                var original=(GeometryModel3D)baseline.Textured.Children[part++];
                Brush brush=new SolidColorBrush(Color.FromRgb(154,181,199));
                if(primitive.GetVertexAccessor("TEXCOORD_0")!=null) {
                    var bitmap=ReadTexture(primitive.Material,assetRoot,metadata);
                    if(bitmap!=null)brush=new ImageBrush(bitmap){ViewportUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,1,1),TileMode=TileMode.Tile,Stretch=Stretch.Fill};
                }
                brush.Freeze();
                var material=new MaterialGroup();
                material.Children.Add(new DiffuseMaterial(brush));
                material.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(45,45,45)),32));
                material.Freeze();
                group.Children.Add(new GeometryModel3D(original.Geometry,material){BackMaterial=material});
            }
        }
        group.Freeze();return group;
    }

    internal static BitmapSource? ReadTexture(SharpGLTF.Schema2.Material? material,string assetRoot,Dictionary<string,string[]> metadata) {
        if(material==null)return null;
        if(material.FindChannel("BaseColor")?.Texture?.PrimaryImage is { } image) {
            using var stream=image.Content.Open();return Decode(stream);
        }
        if(material.Name==null||!metadata.TryGetValue(material.Name,out var files)||files.Length!=1)return null;
        using var doc=JsonDocument.Parse(File.ReadAllText(files[0]));
        if(!doc.RootElement.TryGetProperty("Textures",out var textures))return null;
        if(!textures.TryGetProperty("BaseColor",out var reference)&&!textures.TryGetProperty("PM_Diffuse",out reference))return null;
        var value=reference.GetString();
        if(string.IsNullOrEmpty(value)||!value.StartsWith("/Game/",StringComparison.OrdinalIgnoreCase))return null;
        var relative=value.Split('.')[0][6..].Replace('/',Path.DirectorySeparatorChar)+".png";
        var file=Path.GetFullPath(Path.Combine(assetRoot,"Aion2","Content",relative));
        var prefix=Path.GetFullPath(assetRoot).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(!file.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))return null;
        if(!File.Exists(file)) {
            // Flattened exports can also be inspected by the same loader.
            file=Path.Combine(assetRoot,Path.GetFileName(relative));
            if(!File.Exists(file))return null;
        }
        using var input=File.OpenRead(file);return Decode(input);
    }
    static BitmapSource Decode(Stream stream) {
        var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth=2048;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();return bitmap;
    }
}
