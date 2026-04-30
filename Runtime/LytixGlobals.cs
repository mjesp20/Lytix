using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LytixInternal
{
    public static class LytixGlobals
    {
        public static string name = "Lytix";
        public static string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        public static string folderPath = Path.Combine(documentsPath, name, Application.productName);


        static JsonSerializerSettings settings = new JsonSerializerSettings
        {
            Culture = System.Globalization.CultureInfo.InvariantCulture
        };


        public static List<List<LytixEntry.Entry>> LoadFromFolder()
        {
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }
            string[] filePaths = Directory.GetFiles(folderPath);
            if (filePaths.Length == 0)
            {
                return new List<List<LytixEntry.Entry>>();
                throw new Exception("No files in folder");
            }

            List<List<LytixEntry.Entry>> parsedFiles = new List<List<LytixEntry.Entry>>();
            foreach (string filePath in filePaths)
            {
                parsedFiles.Add(LoadFromFile(filePath));
            }
            return parsedFiles;
        }
        public static List<LytixEntry.Entry> LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new Exception("File not found");
            }
            List<LytixEntry.Entry> parsedLines = new List<LytixEntry.Entry>();
            foreach (string entry in File.ReadLines(filePath))
            {
                parsedLines.Add(ParseLine(entry));
            }
            return parsedLines;
        }
        public static LytixEntry.Entry ParseLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            try
            {
                LytixEntry.Entry entry = JsonConvert.DeserializeObject<LytixEntry.Entry>(line, settings);
                return FilterEntry(entry);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to parse line: {line}\nError: {e.Message}");
                return null;
            }
        }

        private static LytixEntry.Entry FilterEntry(LytixEntry.Entry entry)
        {
            return entry; //todo
            /*
            foreach (KeyValuePair<string, object> arg in entry.args)
            {
                if (arg.Key == "note" || arg.Key == "event" || arg.Key == "prompt")
                {
                    continue;
                }

                var flags = QAToolGlobals.flagTypes ?? new Dictionary<string, Type>();


                if (!flags.ContainsKey(arg.Key))
                {
                    object normalized = QAToolGlobals.NormalizeType(arg.Value);
                    flags[arg.Key] = normalized != null ? normalized.GetType() : typeof(object);
                }


                QAToolGlobals.flagTypes = flags;

                if (!QAToolGlobals.FlagFilters.ContainsKey(arg.Key))
                    continue;

                QAToolGlobals.FlagFilter filter = QAToolGlobals.FlagFilters[arg.Key];

                if (!filter.enabled)
                    continue;

                // If filter value is null, skip comparison
                if (filter.value == null)
                    continue;

                if (arg.Value == null)
                    continue;

                object entryVal = QAToolGlobals.NormalizeType(arg.Value);
                object filterVal = QAToolGlobals.NormalizeType(filter.value);

                if (filterVal == null)
                    continue;


                // Both must be IComparable for ordered comparisons
                IComparable comparable = entryVal as IComparable;

                bool pass = true;

                switch (filter.op)
                {
                    case QAToolGlobals.FilterOperator.Ignore:
                        pass = true;
                        break;

                    case QAToolGlobals.FilterOperator.Equal:
                        pass = entryVal?.Equals(filterVal) ?? false;
                        break;

                    case QAToolGlobals.FilterOperator.NotEqual:
                        pass = !(entryVal?.Equals(filterVal) ?? false);
                        break;

                    case QAToolGlobals.FilterOperator.GreaterThan:
                        pass = comparable != null && comparable.CompareTo(filterVal) > 0;
                        break;

                    case QAToolGlobals.FilterOperator.GreaterThanOrEqual:
                        pass = comparable != null && comparable.CompareTo(filterVal) >= 0;
                        break;

                    case QAToolGlobals.FilterOperator.LessThan:
                        pass = comparable != null && comparable.CompareTo(filterVal) < 0;
                        break;

                    case QAToolGlobals.FilterOperator.LessThanOrEqual:
                        pass = comparable != null && comparable.CompareTo(filterVal) <= 0;
                        break;

                    default:
                        pass = true;
                        break;
                }

                // If any active filter fails, discard the entry
                if (!pass) return null;
            }

            return entry;
        
            */
        }
    }
}

