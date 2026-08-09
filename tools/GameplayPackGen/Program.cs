using AomrModEditor.GameData;
using AomrModEditor.Persistence;

if (args.Length < 2) { Console.Error.WriteLine("Usage: GameplayPackGen <sourceDir> <out.gz>"); return 1; }
var importer = new GameplayImporter(new SecureXmlLoader());
var doc = GameplayPackBuilder.FromImporter(importer, args[0]);
GameplayPackBuilder.WriteGzip(doc, args[1]);
Console.WriteLine($"entities={doc.Entities.Count} out={args[1]}");
return 0;
