using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Aion2Importer;

// Translate presentation text, keeping catalog keys, filter values and asset paths unchanged.
public static class Localization {
    sealed class State {public string Source="",Output="";public bool Updating,Attached;}
    static readonly ConditionalWeakTable<DependencyObject,State> states=new();
    static readonly DependencyPropertyDescriptor textProperty=DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty,typeof(TextBlock));
    static readonly DependencyPropertyDescriptor tooltipProperty=DependencyPropertyDescriptor.FromProperty(FrameworkElement.ToolTipProperty,typeof(FrameworkElement));
    static readonly Dictionary<string,string> english=Load("en");
    static readonly Dictionary<string,string> portuguese=Load("pt");
    static readonly Regex enPattern=Pattern(english),ptPattern=Pattern(portuguese);
    public static string Language {get;private set;}="pt-BR";
    public static readonly DependencyProperty EnabledProperty=DependencyProperty.RegisterAttached("Enabled",typeof(bool),typeof(Localization),new FrameworkPropertyMetadata(false,FrameworkPropertyMetadataOptions.Inherits,EnabledChanged));
    public static void SetEnabled(DependencyObject target,bool value)=>target.SetValue(EnabledProperty,value);
    public static bool GetEnabled(DependencyObject target)=>(bool)target.GetValue(EnabledProperty);
    static void EnabledChanged(DependencyObject target,DependencyPropertyChangedEventArgs args){
        if(target is not FrameworkElement element)return;
        if((bool)args.NewValue){element.Loaded+=Loaded;element.Unloaded+=Unloaded;if(element.IsLoaded)Loaded(element,new());}
        else{element.Loaded-=Loaded;element.Unloaded-=Unloaded;Unloaded(element,new());}
    }
    static Dictionary<string,string> Load(string language){
        using var stream=typeof(Localization).Assembly.GetManifestResourceStream("Aion2Importer.Translations.json")!;
        using var document=JsonDocument.Parse(stream);
        return document.RootElement.GetProperty(language).EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.GetString()!,StringComparer.Ordinal);
    }
    static Regex Pattern(Dictionary<string,string> entries)=>new("(?<![\\p{L}\\p{N}_/\\\\])(?:"+string.Join("|",entries.Keys.OrderByDescending(k=>k.Length).Select(Regex.Escape))+")(?![\\p{L}\\p{N}_/\\\\])",RegexOptions.Compiled);
    public static string T(string source){
        if(string.IsNullOrEmpty(source))return source;
        var entries=Language=="en"?english:portuguese;
        if(entries.TryGetValue(source,out var exact))return exact;
        return (Language=="en"?enPattern:ptPattern).Replace(source,m=>entries[m.Value]);
    }
    static void Loaded(object sender,RoutedEventArgs args){
        if(sender is not FrameworkElement element)return;
        var state=states.GetOrCreateValue(element);if(state.Attached)return;state.Attached=true;
        tooltipProperty.AddValueChanged(element,TooltipChanged);
        if(element is TextBlock text&&Eligible(text)){textProperty.AddValueChanged(text,TextChanged);Translate(text,TextBlock.TextProperty);}
        TranslateTooltip(element);
        if(element is Window window)window.Title=T(window.Title);
    }
    static bool Eligible(TextBlock text){
        if(text.Inlines.Count>1)return false;
        var path=BindingOperations.GetBinding(text,TextBlock.TextProperty)?.Path?.Path;
        return path!="Path"&&!(path=="Name"&&text.DataContext is Asset)&&text.Name!="AssetTitle";
    }
    static void Unloaded(object sender,RoutedEventArgs args){
        if(sender is not FrameworkElement element)return;
        if(!states.TryGetValue(element,out var state)||!state.Attached)return;state.Attached=false;
        tooltipProperty.RemoveValueChanged(element,TooltipChanged);
        if(element is TextBlock text)textProperty.RemoveValueChanged(text,TextChanged);
    }
    static void TextChanged(object? sender,EventArgs args){if(sender is TextBlock text)Translate(text,TextBlock.TextProperty);}
    static void TooltipChanged(object? sender,EventArgs args){if(sender is FrameworkElement element)TranslateTooltip(element);}
    static readonly ConditionalWeakTable<DependencyObject,State> tooltipStates=new();
    static void TranslateTooltip(FrameworkElement element){if(element.ToolTip is string)Translate(element,FrameworkElement.ToolTipProperty,tooltipStates);}
    static void Translate(DependencyObject target,DependencyProperty property,ConditionalWeakTable<DependencyObject,State>? table=null){
        var state=(table??states).GetOrCreateValue(target);if(state.Updating)return;
        if(target.GetValue(property) is not string current)return;
        if(current!=state.Output)state.Source=current;
        var translated=T(state.Source);state.Output=translated;
        if(current==translated)return;
        state.Updating=true;try{target.SetCurrentValue(property,translated);}finally{state.Updating=false;}
    }
    public static void SetLanguage(string language){
        Language=language=="en"?"en":"pt-BR";
        var culture=CultureInfo.GetCultureInfo(Language=="en"?"en-US":"pt-BR");
        CultureInfo.CurrentCulture=culture;CultureInfo.CurrentUICulture=culture;
        CultureInfo.DefaultThreadCurrentCulture=culture;CultureInfo.DefaultThreadCurrentUICulture=culture;
        foreach(var entry in states){
            if(entry.Key is not FrameworkElement element)continue;
            if(!element.IsLoaded)continue;
            if(element is TextBlock text&&Eligible(text))Translate(text,TextBlock.TextProperty);
            TranslateTooltip(element);
        }
    }
}
