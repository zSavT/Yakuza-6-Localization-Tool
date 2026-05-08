using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PoConverter
{
    class PoConverter
    {
        static void Main(string[] args)
        {
            if (args.Length != 3)
            {
                Console.WriteLine("Usage: PoConverter <intput_file> <output_file> <typeConvertion>");
                return;
            } else if (args[0] == "--help" || args[0] == "-h")
            {
                Console.WriteLine("Usage: PoConverter <intput_file> <output_file> <typeConvertion>");
                Console.WriteLine("typeConvertion: po2json or json2po");
                return;
            }
            string inputFile = args[0];
            string outputFile = args[1];
            string typeConvertion = args[2];
            checkArgs(inputFile, outputFile, typeConvertion);
            if(typeConvertion == "po2json")
            {      
                //PoToJson(inputFile, outputFile);
            } else if (typeConvertion == "json2po")
            {
                JsonToPo(inputFile, outputFile);
            }

        }
        private static void checkArgs(string inputFile, string outputFile, string typeConvertion)
        {
            if (!File.Exists(inputFile))
            {
                Console.WriteLine($"File {inputFile} does not exist.");
                return;
            }
            if (File.Exists(outputFile))
            {
                Console.WriteLine($"File {outputFile} already exists.");
                return;
            }
            if (typeConvertion != "po2json" && typeConvertion != "json2po")
            {
                Console.WriteLine($"Type convertion {typeConvertion} is not valid.");
                return;
            }
        }

        private static void JsonToPo(string inputFile, string outputFile)
        {
            string jsonString = File.ReadAllText(inputFile);
            JsonObject jsonObject = JsonNode.Parse(jsonString).AsObject();
            StringBuilder poContent = new StringBuilder();
            foreach (var item in jsonObject)
            {
                poContent.AppendLine($"msgid \"{item.Key}\"");
                poContent.AppendLine($"msgstr \"{item.Value}\"");
                poContent.AppendLine();
            }
            File.WriteAllText(outputFile, poContent.ToString());
        }
    }
}
