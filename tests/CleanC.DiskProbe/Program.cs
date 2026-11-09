using CleanC.Core;
using System.Text.Json;
var report=DiskInventory.Scan(args.Length>1?args[1]:@"C:\",CancellationToken.None);
File.WriteAllText(args[0],JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"FILES={report.ReadFiles} UNREADABLE={report.Unreadable} LIMITED={report.Limited}");
foreach(var d in report.Directories.Take(20))Console.WriteLine($"{d.SizeText} {d.Path}");
