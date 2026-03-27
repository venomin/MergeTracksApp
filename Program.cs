using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

// --- Define directories ---
string baseDir = AppDomain.CurrentDomain.BaseDirectory;
string inputFolder = Path.Combine(baseDir, "input");
string outputFolder = Path.Combine(baseDir, "output");

// --- Check input folder ---
if (!Directory.Exists(inputFolder))
{
    Directory.CreateDirectory(inputFolder);
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"The 'input' folder was not found.\nTherefore it was automatically created: {inputFolder}");
    Console.WriteLine($"Please put your files, you want to merge into the folder and restart this application");
    Console.ResetColor();
    WaitAndExit();
    return;
}

// --- Search for files ---
var inputFiles = Directory
    .GetFiles(inputFolder)
    .Where(f => f.EndsWith(".gpx", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".kml", StringComparison.OrdinalIgnoreCase))
    .ToList();

if (!inputFiles.Any())
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"No .gpx or .kml files found in the folder '{inputFolder}'.");
    Console.WriteLine($"Please put your files, you want to merge into the folder and restart this application");
    Console.ResetColor();
    WaitAndExit();
    return;
}

// --- PROMPT FOR OUTPUT FORMAT (SINGLE KEY PRESS) ---
Console.WriteLine("Which output format do you want to create? (Press 'G' for GPX or 'K' for KML)");
string outputFormat = "";

while (true)
{
    var keyInfo = Console.ReadKey(intercept: true);
    char key = char.ToLower(keyInfo.KeyChar);

    if (key == 'g')
    {
        outputFormat = "gpx";
        Console.WriteLine("> GPX selected.\n");
        break;
    }
    else if (key == 'k')
    {
        outputFormat = "kml";
        Console.WriteLine("> KML selected.\n");
        break;
    }
}

Console.WriteLine($"Found {inputFiles.Count} file(s) in the 'input' folder.");

// --- Create output folder ---
if (!Directory.Exists(outputFolder))
{
    Directory.CreateDirectory(outputFolder);
    Console.WriteLine($"Output folder created: {outputFolder}");
}

// --- Generate filename ---
string timestamp = DateTime.Now.ToString("dd.MM.yy_HH-mm");
string extension = $".{outputFormat}";
string baseFilename = timestamp;
string outputFile = Path.Combine(outputFolder, baseFilename + extension);

int counter = 2;
while (File.Exists(outputFile))
{
    outputFile = Path.Combine(outputFolder, $"{baseFilename}_{counter}{extension}");
    counter++;
}

// List to hold independent tracks
List<TrackData> extractedTracks = new();

// --- Process files ---
foreach (var file in inputFiles)
{
    string fileName = Path.GetFileName(file);
    string baseName = Path.GetFileNameWithoutExtension(file);
    string ext = Path.GetExtension(file).ToLower();

    Console.WriteLine($"  - Processing: {fileName}...");

    try
    {
        XDocument doc = XDocument.Load(file);

        if (ext == ".gpx")
        {
            // Finde alle separaten Tracks (<trk>) in der Datei, unabhängig vom XML Namespace
            var trkNodes = doc.Descendants().Where(e => e.Name.LocalName == "trk").ToList();
            var rteNodes = doc.Descendants().Where(e => e.Name.LocalName == "rte").ToList();

            if (trkNodes.Any())
            {
                foreach (var trk in trkNodes)
                {
                    // Versuche den Originalnamen zu erhalten
                    string trkName = trk.Elements().FirstOrDefault(e => e.Name.LocalName == "name")?.Value ?? baseName;
                    var track = new TrackData { Name = trkName };

                    var pts = trk.Descendants().Where(e => e.Name.LocalName == "trkpt").ToList();
                    foreach (var pt in pts)
                    {
                        string lat = pt.Attribute("lat")?.Value ?? "";
                        string lon = pt.Attribute("lon")?.Value ?? "";
                        if (!string.IsNullOrEmpty(lat) && !string.IsNullOrEmpty(lon))
                            track.Points.Add(new Coordinate { Lat = lat, Lon = lon });
                    }

                    if (track.Points.Any())
                        extractedTracks.Add(track);
                }
            }

            // Falls es sich um Routen (<rte>) handelt
            if (rteNodes.Any())
            {
                foreach (var rte in rteNodes)
                {
                    string rteName = rte.Elements().FirstOrDefault(e => e.Name.LocalName == "name")?.Value ?? baseName;
                    var track = new TrackData { Name = rteName };

                    var pts = rte.Descendants().Where(e => e.Name.LocalName == "rtept").ToList();
                    foreach (var pt in pts)
                    {
                        string lat = pt.Attribute("lat")?.Value ?? "";
                        string lon = pt.Attribute("lon")?.Value ?? "";
                        if (!string.IsNullOrEmpty(lat) && !string.IsNullOrEmpty(lon))
                            track.Points.Add(new Coordinate { Lat = lat, Lon = lon });
                    }

                    if (track.Points.Any())
                        extractedTracks.Add(track);
                }
            }

            // Fallback, falls die Datei weder <trk> noch <rte> hat (z.B. defekte Struktur)
            if (!trkNodes.Any() && !rteNodes.Any())
            {
                var allPts = doc.Descendants().Where(e => e.Name.LocalName == "trkpt").ToList();
                if (allPts.Any())
                {
                    var track = new TrackData { Name = baseName };
                    foreach (var pt in allPts)
                    {
                        string lat = pt.Attribute("lat")?.Value ?? "";
                        string lon = pt.Attribute("lon")?.Value ?? "";
                        if (!string.IsNullOrEmpty(lat) && !string.IsNullOrEmpty(lon))
                            track.Points.Add(new Coordinate { Lat = lat, Lon = lon });
                    }
                    extractedTracks.Add(track);
                }
            }
        }
        else if (ext == ".kml")
        {
            var placemarks = doc.Descendants().Where(e => e.Name.LocalName == "Placemark").ToList();

            foreach (var pm in placemarks)
            {
                var lineStrings = pm.Descendants().Where(e => e.Name.LocalName == "LineString").ToList();
                if (!lineStrings.Any()) continue;

                string pmName = pm.Elements().FirstOrDefault(e => e.Name.LocalName == "name")?.Value ?? baseName;

                int lsIndex = 1;
                foreach (var ls in lineStrings)
                {
                    var coordsNode = ls.Descendants().FirstOrDefault(e => e.Name.LocalName == "coordinates");
                    if (coordsNode == null || string.IsNullOrWhiteSpace(coordsNode.Value)) continue;

                    var track = new TrackData { Name = lineStrings.Count > 1 ? $"{pmName}_{lsIndex}" : pmName };

                    var points = coordsNode.Value.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var point in points)
                    {
                        if (point.Contains(","))
                        {
                            var parts = point.Split(',');
                            track.Points.Add(new Coordinate { Lon = parts[0].Trim(), Lat = parts[1].Trim() });
                        }
                    }

                    if (track.Points.Any())
                    {
                        extractedTracks.Add(track);
                        lsIndex++;
                    }
                }
            }
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"Warning: Error processing file '{fileName}'. {ex.Message}");
        Console.ResetColor();
    }
}

// --- Create Output File ---
if (extractedTracks.Any())
{
    if (outputFormat == "gpx")
    {
        CreateGpxOutput(extractedTracks, timestamp, outputFile);
    }
    else if (outputFormat == "kml")
    {
        CreateKmlOutput(extractedTracks, timestamp, outputFile);
    }
}
else
{
    Console.WriteLine("No valid track data was found in the processed files.");
}

WaitAndExit();

// =========================================================================
// METHODS & CLASSES 
// =========================================================================

static void CreateGpxOutput(List<TrackData> tracks, string timestamp, string outputFile)
{
    XNamespace gpxNs = "http://www.topografix.com/GPX/1/1";
    List<XElement> gpxTracks = new();

    foreach (var track in tracks)
    {
        var trkPts = track.Points.Select(p => new XElement(gpxNs + "trkpt",
            new XAttribute("lat", p.Lat),
            new XAttribute("lon", p.Lon)
        ));

        gpxTracks.Add(new XElement(gpxNs + "trk",
            new XElement(gpxNs + "name", track.Name),
            new XElement(gpxNs + "trkseg", trkPts)
        ));
    }

    var newDoc = new XDocument(
        new XDeclaration("1.0", "utf-8", null),
        new XElement(gpxNs + "gpx",
            new XAttribute("version", "1.1"),
            new XAttribute("creator", "MergeTracks .NET App"),
            new XElement(gpxNs + "metadata",
                new XElement(gpxNs + "name", $"Track Collection ({timestamp})")
            ),
            gpxTracks
        )
    );

    SaveXmlDocument(newDoc, outputFile);
}

static void CreateKmlOutput(List<TrackData> tracks, string timestamp, string outputFile)
{
    XNamespace kmlNs = "http://www.opengis.net/kml/2.2";
    List<XElement> placemarks = new();

    foreach (var track in tracks)
    {
        string coordinateString = string.Join(" ", track.Points.Select(p => $"{p.Lon},{p.Lat},0"));

        placemarks.Add(
            new XElement(kmlNs + "Placemark",
                new XElement(kmlNs + "name", track.Name),
                new XElement(kmlNs + "styleUrl", "#line-style"),
                new XElement(kmlNs + "LineString",
                    new XElement(kmlNs + "tessellate", "1"),
                    new XElement(kmlNs + "coordinates", coordinateString)
                )
            )
        );
    }

    var newDoc = new XDocument(
        new XDeclaration("1.0", "utf-8", null),
        new XElement(kmlNs + "kml",
            new XElement(kmlNs + "Document",
                new XElement(kmlNs + "name", $"Track Collection ({timestamp})"),
                new XElement(kmlNs + "Style", new XAttribute("id", "line-style"),
                    new XElement(kmlNs + "LineStyle",
                        new XElement(kmlNs + "color", "7f0000ff"),
                        new XElement(kmlNs + "width", "4")
                    )
                ),
                placemarks
            )
        )
    );

    SaveXmlDocument(newDoc, outputFile);
}

static void SaveXmlDocument(XDocument doc, string outputFile)
{
    try
    {
        XmlWriterSettings settings = new XmlWriterSettings { Indent = true };
        using (XmlWriter writer = XmlWriter.Create(outputFile, settings))
        {
            doc.Save(writer);
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\nSuccess! The file has been saved to:\n{outputFile}");
        Console.ResetColor();
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Error saving the file: {ex.Message}");
        Console.ResetColor();
    }
}

static void WaitAndExit()
{
    Console.WriteLine("\nPress any key to exit...");
    Console.ReadKey();
}

// --- Data Models ---
// Komplett vereinfacht, keine Segmente mehr! Jeder Track bleibt ein eigener Track.
class TrackData
{
    public string Name { get; set; } = string.Empty;
    public List<Coordinate> Points { get; set; } = new();
}

class Coordinate
{
    public string Lat { get; set; } = string.Empty;
    public string Lon { get; set; } = string.Empty;
}