using GenerateEncodingsMap;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Text;

public static class Program
{
    static void Main(string[] args)
    {
        var contents = File.ReadAllText(@"mdn_text_decodings.txt");
        var mappedEncodings = JsonConvert.DeserializeObject<List<MappedEncoding>>(contents);

        var mappedEncodingsByName = mappedEncodings.ToDictionary(me => me.name);

        var mdnAliases = new List<EncodingAlias>();
        foreach(var mappedEncoding in mappedEncodings)
        {
            mdnAliases.Add(new EncodingAlias(mappedEncoding.name, mappedEncoding.aliases.Select(a=>a.ToLowerInvariant())));
        }

        var dotnetAliases = new List<EncodingAlias>();
        //Without this line, Encoding.GetEncodings() returns a basic set.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var allEncodings = Encoding.GetEncodings();
        var dotnetEncodingsByName = allEncodings.ToDictionary(e => e.Name);
        foreach (var dotnetEncodingInfo in allEncodings)
        {
            var dotnetEncoding = dotnetEncodingInfo.GetEncoding();

            dotnetAliases.Add(new EncodingAlias(
                dotnetEncodingInfo.Name,
                new string[]
                {
                    dotnetEncodingInfo.Name,
                    dotnetEncoding.EncodingName,
                    dotnetEncoding.BodyName,
                    dotnetEncoding.HeaderName,
                    dotnetEncoding.WebName
                }.Select(s=>s.ToLowerInvariant())));
            //Debug.WriteLine(dotnetEncoding.EncodingName);
        }

        //Now match them up
        foreach(var mdnAlias in mdnAliases)
        {
            foreach(var dotnetAlias in dotnetAliases)
            {
                if(mdnAlias.Aliases.Intersect(dotnetAlias.Aliases).Any())
                {
                    var e = dotnetEncodingsByName[dotnetAlias.CanonicalName].GetEncoding();
                    
                    Console.WriteLine($"Mapping MDN name {mdnAlias.CanonicalName} to .NET {dotnetAlias.CanonicalName}. Is single byte? {e.IsSingleByte}\"");
                    Debug.WriteLine($"Mapping MDN name {mdnAlias.CanonicalName} to .NET {dotnetAlias.CanonicalName}. Is single byte? {e.IsSingleByte} Is Normalized? {e.IsAlwaysNormalized()}");
                    var matchEncoding = mappedEncodingsByName[mdnAlias.CanonicalName];
                    matchEncoding.dotnet_name = dotnetAlias.CanonicalName;

                    if(e.IsSingleByte)
                    {
                        matchEncoding.codePoints = new List<UInt32>();
                        matchEncoding.bytesForCodePoints = new List<UInt32[]>();
                        //Here i is the input byte, which we get the code point from
                        for(int i=0; i<256; i++)
                        {
                            var outputChar = e.GetChars(new byte[] {  (byte)i });
                            Debug.Assert(outputChar.Length == 1);
                            matchEncoding.codePoints.Add((UInt32)outputChar[0]);
                            var outputByte = e.GetBytes(outputChar);
                            Debug.Assert(outputByte.Length == 1);
                            Debug.Assert(outputByte[0] == i);
                            matchEncoding.bytesForCodePoints.Add(outputByte.Select(ob=>(UInt32)ob).ToArray());
                        }
                    }
                    else
                    {
                        if (!mdnAlias.CanonicalName.ToLowerInvariant().Contains("utf-8"))
                        {
                            var encodingThatPops = Encoding.GetEncoding(
                                dotnetAlias.CanonicalName,
                                EncoderFallback.ExceptionFallback,
                                DecoderFallback.ExceptionFallback
                                );
                            matchEncoding.codePoints = new List<UInt32>();
                            matchEncoding.bytesForCodePoints = new List<UInt32[]>();
                            //Here is directly the code point
                            for (int i = 0; i < 65536; /*End of Unicode BMP*/ i++)
                            {
                                try
                                {
                                    var outputBytes = encodingThatPops.GetBytes(new char[] { (char)i });
                                    matchEncoding.bytesForCodePoints.Add(outputBytes.Select(ob => (UInt32)ob).ToArray());
                                    matchEncoding.codePoints.Add((UInt32)i);
                                }
                                catch (EncoderFallbackException) { }
                            }
                        }
                    }

                    break;
                }
            }
            if (string.IsNullOrEmpty(mappedEncodingsByName[mdnAlias.CanonicalName].dotnet_name))
            {
                Console.WriteLine($"Unable to match MDN name {mdnAlias.CanonicalName}");
                Debug.WriteLine($"Unable to match MDN name {mdnAlias.CanonicalName}");
            }
        }

        //Now remove unmatched/invalid ones
        for(int i=mappedEncodings.Count - 1; i>=0; i--)
        {
            if (string.IsNullOrEmpty(mappedEncodings[i].dotnet_name)
                || mappedEncodings[i].codePoints == null
                || mappedEncodings[i].bytesForCodePoints == null
                )
            {
                mappedEncodings.RemoveAt(i);
            }
        }

        var outputContents = "let TEXT_ENCODINGS_RAW = " + JsonConvert.SerializeObject(mappedEncodings) + ";";
        File.WriteAllText(@"encoders_data.js", outputContents);

        GenerateTestHTML(mappedEncodings, @"verify_encodings");
    }

    /// <summary>
    /// Generate a test set consisting of the following:
    /// - An overview HTML page with expected outputs for encodings, encoded by code point into UTF-8
    ///     - Each encoding will have an iframe pointing to an inner document
    ///     - The inner document will be encoding using the tested encoding and marked as such
    /// - Comparison of UTF-8 expected output and iframe actual output should match
    /// </summary>
    /// <param name="mappedEncodings"></param>
    /// <param name="where">The directory for the test report HTML</param>
    private static void GenerateTestHTML(List<MappedEncoding> mappedEncodings, string where)
    {
        Directory.CreateDirectory(where);
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>\r\n<html> <head><meta http-equiv=\"Content-Type\" content=\"text/html;charset=\"utf-8\"> \r\n<style> iframe { \tborder: none; \tmargin: 0; \tpadding: 0; width: 100%; } </style></head> <body>\r\n");
        builder.AppendLine("<h1>Overview</h1>");
        builder.AppendLine("Tests show the UTF-8-based mapping on the first line, then test the encoding via a meta charset style declaration in an iframe following. The two should generally match.");
        builder.AppendLine("Known caveats: &lt; is skipped, apparent but OK mismatches will appear when the UTF-8 displays the codepoint in a box but the encoding displays only the diamond, and multi-character and UTF-16-based variants don't display the test correctly.");
        foreach (var mappedEncoding in mappedEncodings)
        {
            builder.AppendLine("<hr />");
            builder.AppendLine("<h2> Web Name "+mappedEncoding.name + "/.NET Name " + mappedEncoding.dotnet_name + "</h2><br />");
            builder.AppendLine(GenerateUtf8ExpectedOutput(mappedEncoding));
            builder.AppendLine($"<iframe src=\"{mappedEncoding.name}.html\"></iframe>");
            builder.AppendLine();
            var encodedBytes = GenerateActualOutput(mappedEncoding);
            File.WriteAllBytes(Path.Combine(where, mappedEncoding.name + ".html"), encodedBytes);
        }
        builder.AppendLine("</body></html>");
        File.WriteAllText(Path.Combine(where, "index.html"), builder.ToString());
    }

    /// <summary>
    /// Generates the HTML needed for the 
    /// </summary>
    /// <returns></returns>
    private static string GenerateUtf8ExpectedOutput(MappedEncoding mappedEncoding)
    {
        var builder = new StringBuilder();
        for(int i=0; i<mappedEncoding.codePoints.Count; i++)
        {
            if ((char)mappedEncoding.codePoints[i] == '<')
                continue;
            builder.Append((char)mappedEncoding.codePoints[i]);
        }
        return builder.ToString();
    }

    private static byte[] GenerateActualOutput(MappedEncoding mappedEncoding)
    {
        var encoding = Encoding.GetEncoding(mappedEncoding.dotnet_name);
        var header = $"<!doctype html>\r\n<html><head><meta http-equiv=\"Content-Type\" content=\"text/html;charset={mappedEncoding.name}\">\r\n<style> body {{ margin: 0; padding: 0; }} </style></head>\r\n<body>";
        var outputBytes = encoding.GetBytes(header).ToList();
        for(int i=0; i<mappedEncoding.bytesForCodePoints.Count; i++)
        {
            if ((char)mappedEncoding.codePoints[i] == '<')
                continue;
            var b = mappedEncoding.bytesForCodePoints[i];
            for (int j = 0; j < b.Length; j++)
                outputBytes.Add((byte)b[j]);
        }
        var footer = "</body></html>";
        outputBytes.AddRange(encoding.GetBytes(footer));
        return outputBytes.ToArray();
    }

}
