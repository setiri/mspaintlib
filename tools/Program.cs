using Mono.Cecil;
using MsPaintFile;
using MsPaintFile.Container;
using MsPaintFile.Container.Boxes;
using MsPaintFile.Items;

if (args.Length == 0)
{
    Console.WriteLine("Usage: inspect dll <path-to-dll> <type-name-substring>");
    Console.WriteLine("       inspect dump <path-to-paint-file>");
    return 1;
}

if (args[0] == "roundtrip")
{
    var doc = PaintDocument.Load(args[1]);
    doc.Save(args[2]);
    Console.WriteLine($"Wrote {new FileInfo(args[2]).Length} bytes to {args[2]}");
    return 0;
}

if (args[0] == "dump")
{
    using var fs = File.OpenRead(args[1]);
    var reader = new BoxReader(fs);
    BoxHeader meta = default;
    bool found = false;
    while (reader.TryReadBoxHeader(reader.Length, out var box))
    {
        Console.WriteLine($"top: {box.Type} size={box.PayloadEnd - box.HeaderStart} payload={box.PayloadStart}..{box.PayloadEnd}");
        if (box.Type == FourCc.Of("meta")) { meta = box; found = true; }
        reader.Position = box.PayloadEnd;
    }
    if (!found) { Console.WriteLine("no meta"); return 0; }
    var table = MetaParser.Parse(reader, meta);
    Console.WriteLine($"Primary: {table.PrimaryItemId}");
    Console.WriteLine($"Idat: {table.IdatBytes.Length} bytes");
    foreach (var kv in table.Items)
    {
        var id = kv.Key; var item = kv.Value;
        Console.WriteLine($"Item {id} type={item.Type} cm={item.ConstructionMethod} extents=[{string.Join(",", item.Extents.Select(e => $"({e.Offset},{e.Length})"))}]");
        foreach (var p in item.Properties)
            Console.WriteLine($"  prop: {p}");
    }
    foreach (var r in table.References)
        Console.WriteLine($"Ref {r.Type} from={r.FromItemId} to=[{string.Join(",", r.ToItemIds)}]");
    return 0;
}

if (args[0] == "dll")
{
    var asm = AssemblyDefinition.ReadAssembly(args[1]);
    foreach (var module in asm.Modules)
    {
        foreach (var type in module.GetTypes())
        {
            if (!type.IsPublic) continue;
            if (!type.Name.Contains(args[2], StringComparison.OrdinalIgnoreCase)) continue;
            Console.WriteLine($"{(type.IsInterface ? "interface" : type.IsAbstract ? "abstract class" : "class")} {type.FullName}");
            if (type.BaseType != null && type.BaseType.FullName != "System.Object")
                Console.WriteLine($"   : {type.BaseType.FullName}");
            foreach (var iface in type.Interfaces) Console.WriteLine($"   , {iface.InterfaceType.FullName}");
            foreach (var ctor in type.Methods.Where(m => m.IsConstructor && (m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly)))
                Console.WriteLine($"  {(ctor.IsFamily ? "protected" : ctor.IsFamilyOrAssembly ? "protected internal" : "public")} ctor({string.Join(", ", ctor.Parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"))})");
            foreach (var m in type.Methods.Where(m => !m.IsConstructor && (m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly) && !m.IsSpecialName))
                Console.WriteLine($"  {(m.IsAbstract ? "abstract " : m.IsVirtual ? "virtual " : "")}{(m.IsFamily ? "protected " : m.IsFamilyOrAssembly ? "protected internal " : "public ")}{m.ReturnType.Name} {m.Name}({string.Join(", ", m.Parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"))})");
            foreach (var p in type.Properties.Where(p => (p.GetMethod?.IsPublic ?? false) || (p.SetMethod?.IsPublic ?? false)))
                Console.WriteLine($"  property {p.PropertyType.Name} {p.Name} {{ {(p.GetMethod != null ? "get; " : "")}{(p.SetMethod != null ? "set; " : "")}}}");
            Console.WriteLine();
        }
    }
    return 0;
}

Console.WriteLine($"Unknown command: {args[0]}");
return 1;
