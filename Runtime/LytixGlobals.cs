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

        // filters

        public enum FilterOperator
        {
            Ignore, Equal, NotEqual,
            GreaterThan, GreaterThanOrEqual,
            LessThan, LessThanOrEqual
        }

        public class FlagFilter
        {
            public bool           enabled;
            public FilterOperator op;
            public object         value;
        }


        //filter state.
        public static Dictionary<string, FlagFilter> FlagFilters = new Dictionary<string, FlagFilter>();

        //Normalises JSON numeric types to readable 32 bit types
        public static object NormalizeType(object input) => input switch
        {
            long   l => (int)l,
            double d => (float)d,
            _        => input
        };

        // avoid "culture issues" (denmark uses , while everyone else uses .)

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
            foreach (string entry in File.ReadLines(filePath).Skip(1)) // Skip header
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
            if (entry?.args == null)
                return entry;

            foreach (KeyValuePair<string, object> arg in entry.args)
            {
                if (!FlagFilters.TryGetValue(arg.Key, out FlagFilter filter))
                    continue;

                if (!filter.enabled || filter.op == FilterOperator.Ignore)
                    continue;

                if (filter.value == null || arg.Value == null)
                    continue;

                object entryVal  = NormalizeType(arg.Value);
                object filterVal = NormalizeType(filter.value);

                if (filterVal == null)
                    continue;

                IComparable comparable = entryVal as IComparable;
                bool pass;

                switch (filter.op)
                {
                    case FilterOperator.Equal:
                        pass = entryVal?.Equals(filterVal) ?? false;
                        break;
                    case FilterOperator.NotEqual:
                        pass = !(entryVal?.Equals(filterVal) ?? false);
                        break;
                    case FilterOperator.GreaterThan:
                        pass = comparable != null && comparable.CompareTo(filterVal) > 0;
                        break;
                    case FilterOperator.GreaterThanOrEqual:
                        pass = comparable != null && comparable.CompareTo(filterVal) >= 0;
                        break;
                    case FilterOperator.LessThan:
                        pass = comparable != null && comparable.CompareTo(filterVal) < 0;
                        break;
                    case FilterOperator.LessThanOrEqual:
                        pass = comparable != null && comparable.CompareTo(filterVal) <= 0;
                        break;
                    default:
                        pass = true;
                        break;
                }

                if (!pass) return null;
            }

            return entry;
        }
    }
}