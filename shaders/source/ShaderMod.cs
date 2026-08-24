using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ImGuiNET;
using RecompOne.Runtime.Hle;
using RecompOne.Runtime.Modding;

//shader mod, it is like minecraft, you provice an folder with the shader and it applies it, the shader needs a settings.json
//this is not sotn specific this works with any recomp powered by recompone in theory
//this also includes a simple port of crt-geom(because i love this shader), credits to the original creators of it!


//shader config
public sealed class ShaderEntry
{
    public string Id = "";
    public string Name = "";
    public string Author = "";
    public string Description = "";
    public string Source = "";
    public string Path = "";
    public readonly Dictionary<string, float> Params = new(StringComparer.Ordinal);
}

public class ShaderMod : IMod
{
    private const string ModId = "shaders";
    private const string Prefix = "mods.shaders.";
    private const string ShadersDir = "shaders";

    private readonly List<ShaderEntry> shaders = new();
    private string selected = "";

    public void OnLoad()
    {
        Discover();
        selected = RecompOne.Runtime.Runtime.View.GetString(Prefix + "selected", "");
        Apply(selected);
    }

    public void OnUnload()
    {
        PostFx.Clear();
    }


    public void DrawSettings()
    {
        var current = Current();

        ImGui.TextUnformatted("Shader");
        ImGui.SameLine();

        ImGui.SetNextItemWidth(200f);
        if (ImGui.BeginCombo("##shader", current?.Name ?? "None"))
        {
            if (ImGui.Selectable("None", current == null)) Select("");
            foreach (var s in shaders)
                if (ImGui.Selectable(s.Name, current != null && current.Id == s.Id)) Select(s.Id);
            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (ImGui.Button("Reload")) Reload();
        ImGui.SameLine();
        if (ImGui.Button("Rescan")) { Discover(); Apply(selected); }

        if (shaders.Count == 0)
            ImGui.TextWrapped($"no shaders was found put a shader folder into the mod's {ShadersDir}/ folder and press rescan!");

        if (current != null)
        {
            if (!string.IsNullOrWhiteSpace(current.Author)) ImGui.TextDisabled($"by {current.Author}");
            if (!string.IsNullOrWhiteSpace(current.Description)) ImGui.TextWrapped(current.Description);
        }

        if (PostFx.Error is { } err)
        {
            Console.WriteLine($"[Shader] Shader failed to compile: {err}");
            ImGui.Separator();
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.4f, 0.4f, 1f), "Shader failed to compile:");
            ImGui.TextWrapped(err);
        }
    }

    private ShaderEntry? Current() => shaders.FirstOrDefault(s => s.Id == selected);

    private void Select(string id)
    {
        selected = id;
        RecompOne.Runtime.Runtime.View.SetString(Prefix + "selected", id);
        RecompOne.Runtime.Runtime.SaveView();
        Apply(id);
    }

    private void Reload()
    {
        Discover();
        Apply(selected);
    }

    private void Apply(string id)
    {
        var entry = shaders.FirstOrDefault(s => s.Id == id);
        if (entry == null)
        {
            PostFx.Clear();
            return;
        }

        PostFx.Set(entry.Source, entry.Name);
        if (entry.Params.Count > 0) PostFx.SetParams(entry.Params);
    }

    private void Discover()
    {
        shaders.Clear();

        string? root = ModLoader.LoadedMods.FirstOrDefault(m => m.Id == ModId)?.SourcePath;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;

        string dir = System.IO.Path.Combine(root, ShadersDir);
        if (!Directory.Exists(dir)) return;

        foreach (var folder in Directory.EnumerateDirectories(dir))
        {
            try
            {
                var entry = Read(folder);
                if (entry != null) shaders.Add(entry);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Shaders] fail to read {System.IO.Path.GetFileName(folder)}: {ex.Message}");
            }
        }

        shaders.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    }

    private ShaderEntry? Read(string folder)
    {
        string id = System.IO.Path.GetFileName(folder);
        var entry = new ShaderEntry { Id = id, Name = id, Path = folder };

        string? fragName = null;
        string settingsPath = System.IO.Path.Combine(folder, "settings.json");

        if (File.Exists(settingsPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath), new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            var r = doc.RootElement;

            if (r.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } name) entry.Name = name;
            if (r.TryGetProperty("author", out var a)) entry.Author = a.GetString() ?? "";
            if (r.TryGetProperty("description", out var d)) entry.Description = d.GetString() ?? "";
            if (r.TryGetProperty("fragment", out var f)) fragName = f.GetString();

            if (r.TryGetProperty("parameters", out var ps) && ps.ValueKind == JsonValueKind.Array) //load params
            {
                foreach (var p in ps.EnumerateArray())
                {
                    if (!p.TryGetProperty("uniform", out var u) || u.GetString() is not { Length: > 0 } uniform) continue;
                    float value = 0f;
                    if (p.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number) value = v.GetSingle();
                    else if (p.TryGetProperty("default", out var dv) && dv.ValueKind == JsonValueKind.Number) value = dv.GetSingle();
                    else continue;
                    entry.Params[uniform] = value;
                }
            }
        }

        string? fragPath = null;
        if (!string.IsNullOrWhiteSpace(fragName))
        {
            string candidate = System.IO.Path.Combine(folder, fragName);
            if (File.Exists(candidate)) fragPath = candidate;
            else Console.Error.WriteLine($"[Shaders] {id}: fragment {fragName} not found");
        }

        fragPath ??= Directory.EnumerateFiles(folder, "*.frag").FirstOrDefault() ?? Directory.EnumerateFiles(folder, "*.glsl").FirstOrDefault();

        if (fragPath == null)
        {
            Console.Error.WriteLine($"[Shaders] {id} has no fragment shader");
            return null;
        }

        entry.Source = File.ReadAllText(fragPath);
        return entry;
    }
}
