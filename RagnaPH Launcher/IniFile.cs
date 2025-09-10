using System.Collections.Generic;

public class IniFile
{
    private readonly Dictionary<string, Dictionary<string, string>> data = new Dictionary<string, Dictionary<string, string>>();

    public IniFile(string[] lines)
    {
        string currentSection = "";

        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith(";") || trimmed.StartsWith("#"))
                continue;

            if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
            {
                currentSection = trimmed.Substring(1, trimmed.Length - 2).Trim();
                data[currentSection] = new Dictionary<string, string>();
            }
            else if (trimmed.Contains("="))
            {
                string[] parts = trimmed.Split(new[] { '=' }, 2);
                data[currentSection][parts[0].Trim()] = parts[1].Trim();
            }
        }
    }

    public string Get(string section, string key, string defaultValue = "")
    {
        if (data.ContainsKey(section) && data[section].ContainsKey(key))
            return data[section][key];
        return defaultValue;
    }

    public Dictionary<string, string> GetSection(string section)
    {
        if (data.ContainsKey(section))
            return new Dictionary<string, string>(data[section]);
        return new Dictionary<string, string>();
    }
}
