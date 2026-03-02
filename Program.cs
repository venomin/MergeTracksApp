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
    Console.WriteLine($"The 'input' folder was not found. And therefore created: {inputFolder}\r\nPut your tracks into the folder and restart the application");
    Console.ResetColor();
    Console.ReadKey();
    return;
}

// --- Search for files ---
var inputFiles = Directory.GetFiles(inputFolder)
    .Where(f => f.EndsWith(".gpx", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".kml", StringComparison.OrdinalIgnoreCase))
    .ToList();

if (!inputFiles.Any())
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"No .gpx or .kml files found in the folder '{inputFolder}'. The program will exit.");
    Console.ResetColor();
    Console.ReadKey();
    return;
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
string baseFilename = $"CombinedTracks_{timestamp}";
string extension = ".gpx";
string outputFile = Path.Combine(outputFolder, baseFilename + extension);

// If file already exists, append an index (e.g., _2.gpx)
int counter = 2;
while (File.Exists(outputFile))
{
    outputFile = Path.Combine(outputFolder, $"{baseFilename}_{counter}{extension}");
    counter++;
}

// --- XML Namespaces ---
XNamespace gpxNs = "http://www.topografix.com/GPX/1/1";
XNamespace kmlNs = "http://www.opengis.net/kml/2.2";

List<XElement> gpxTracks = new();

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
            // Find all trkpt elements and convert them to proper XElements
            var trkPts = doc.Descendants(gpxNs + "trkpt")
                            .Select(p => new XElement(gpxNs + "trkpt",
                                new XAttribute("lat", (string)p.Attribute("lat") ?? ""),
                                new XAttribute("lon", (string)p.Attribute("lon") ?? "")
                            )).ToList();

            if (trkPts.Any())
            {
                gpxTracks.Add(new XElement(gpxNs + "trk",
                    new XElement(gpxNs + "name", baseName),
                    new XElement(gpxNs + "trkseg", trkPts)
                ));
            }
        }
        else if (ext == ".kml")
        {
            // Find all coordinate blocks in KML
            var coordinateNodes = doc.Descendants(kmlNs + "Placemark")
                                     .Descendants(kmlNs + "LineString")
                                     .Descendants(kmlNs + "coordinates")
                                     .ToList();

            int trackIndex = 1;
            foreach (var node in coordinateNodes)
            {
                string rawCoords = node.Value;
                // Split by spaces, tabs, and newlines
                var points = rawCoords.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                List<XElement> trkPts = new();
                foreach (var point in points)
                {
                    if (point.Contains(","))
                    {
                        var parts = point.Split(',');
                        // KML is lon,lat - GPX requires lat="..." lon="..."
                        trkPts.Add(new XElement(gpxNs + "trkpt",
                            new XAttribute("lat", parts[1].Trim()),
                            new XAttribute("lon", parts[0].Trim())
                        ));
                    }
                }

                if (trkPts.Any())
                {
                    string trackName = coordinateNodes.Count > 1 ? $"{baseName}_{trackIndex}" : baseName;
                    gpxTracks.Add(new XElement(gpxNs + "trk",
                        new XElement(gpxNs + "name", trackName),
                        new XElement(gpxNs + "trkseg", trkPts)
                    ));
                    trackIndex++;
                }
            }
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"Warning: Error processing file '{fileName}'. {ex.Message}");
        Console.ResetColor();
        Console.ReadKey();
    }
}

// --- Assemble and save the resulting XML ---
if (gpxTracks.Any())
{
    var newGpxDoc = new XDocument(
        new XDeclaration("1.0", "utf-8", null),
        new XElement(gpxNs + "gpx",
            new XAttribute("version", "1.1"),
            new XAttribute("creator", "MergeTracks .NET App"),
            new XElement(gpxNs + "metadata",
                new XElement(gpxNs + "name", $"Merged Tracks ({timestamp})")
            ),
            gpxTracks
        )
    );

    try
    {
        // Settings for nice indentation (Pretty Print)
        XmlWriterSettings settings = new XmlWriterSettings { Indent = true };
        using (XmlWriter writer = XmlWriter.Create(outputFile, settings))
        {
            newGpxDoc.Save(writer);
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\nSuccess! The merged GPX file has been saved to:\n{outputFile}");
        Console.ResetColor();
        Console.ReadKey();
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Error saving the file: {ex.Message}");
        Console.ResetColor();
        Console.ReadKey();
    }
}
else
{
    Console.WriteLine("No valid track data was found in the processed files.");
    Console.ReadKey();
}