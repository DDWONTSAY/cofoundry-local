using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;
namespace Cofoundry {
public static class Ui {
#if ENGLISH
  public static bool English=true;
#else
  public static bool English=false;
#endif
  static readonly Dictionary<string,string> translations=Load();
  static Dictionary<string,string> Load(){using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Cofoundry.English.json")){if(stream==null)return new Dictionary<string,string>();using(var reader=new StreamReader(stream))return new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(reader.ReadToEnd());}}
  public static string T(string text){string translated;return English&&translations.TryGetValue(text,out translated)?translated:text;}
}
}
