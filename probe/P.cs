using System; using System.IO; using System.Linq; using System.Reflection;
var dir = @"C:\Program Files (x86)\Steam\steamapps\common\Dale&Dawson\BepInEx\";
var paths = Directory.GetFiles(dir+"interop","*.dll").Concat(Directory.GetFiles(dir+"core","*.dll")).Concat(Directory.GetFiles(Path.GetDirectoryName(typeof(object).Assembly.Location),"*.dll"));
using var mlc = new MetadataLoadContext(new PathAssemblyResolver(paths));
var asm = mlc.LoadFromAssemblyPath(dir+@"interop\"+(Environment.GetEnvironmentVariable("ASM") ?? "Assembly-CSharp")+".dll");
if (args[0]=="--enum") { foreach (var f in asm.GetType(args[1]).GetFields().Where(f=>f.IsLiteral)) Console.WriteLine(f.Name+" = "+f.GetRawConstantValue()); return; }
if (args[0]=="--types") { foreach (var t in asm.GetTypes()) if (t.FullName.Contains(args[1])) Console.WriteLine(t.FullName); return; }
if (args[0]=="--prop") { foreach (var t in asm.GetTypes()) foreach (var pr in t.GetProperties(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly)) if (pr.Name==args[1]) Console.WriteLine(t.FullName+" : "+pr.PropertyType+" "+pr.Name); return; }
if (args[0]=="--find") { foreach (var t in asm.GetTypes()) foreach (var m in t.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly)) if (m.Name==args[1]) Console.WriteLine(t.FullName+" : "+m); return; }
foreach (var name in args) {
  var t = asm.GetType(name);
  Console.WriteLine("== "+name);
  foreach (var c in t.GetConstructors()) Console.WriteLine("  ctor("+string.Join(", ", c.GetParameters().Select(p=>p.ParameterType+" "+p.Name))+")");
  foreach (var p in t.GetProperties()) Console.WriteLine("  prop "+p.PropertyType+" "+p.Name);
  foreach (var m in t.GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly).Where(m=>!m.IsSpecialName)) Console.WriteLine("  "+m.ReturnType.Name+" "+m.Name+"("+string.Join(", ", m.GetParameters().Select(p=>p.ParameterType+" "+p.Name))+")");
}
