using System.Globalization;
using System.Text.Json;

namespace DaisysApp.Applets.HomeAssistant;

/// <summary>An entity's state as Home Assistant last sent it: its state text and its attributes.</summary>
public sealed class HaEntity
{
    public string EntityId { get; }
    public string Domain { get; }
    public string State { get; }
    public JsonElement Attributes { get; }
    public DateTime LastChanged { get; }

    public HaEntity(JsonElement s)
    {
        EntityId = HaClient.Str(s, "entity_id") ?? "";
        Domain = EntityId.Contains('.') ? EntityId[..EntityId.IndexOf('.')] : EntityId;
        State = HaClient.Str(s, "state") ?? "";
        Attributes = s.TryGetProperty("attributes", out var a) && a.ValueKind == JsonValueKind.Object ? a : default;
        LastChanged = DateTime.TryParse(HaClient.Str(s, "last_changed"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t.ToLocalTime() : DateTime.MinValue;
    }

    public string FriendlyName => Text("friendly_name") ?? EntityId;
    public bool Unavailable => State == "unavailable";
    public int Features => (int)(Number("supported_features") ?? 0);
    public bool Has(int feature) => (Features & feature) != 0;

    public string? Text(string name) =>
        Attributes.ValueKind == JsonValueKind.Object && Attributes.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public double? Number(string name) =>
        Attributes.ValueKind == JsonValueKind.Object && Attributes.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    public bool? Bool(string name) =>
        Attributes.ValueKind == JsonValueKind.Object && Attributes.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    public List<string> List(string name) =>
        Attributes.ValueKind == JsonValueKind.Object && Attributes.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList() : new();

    public double[]? Numbers(string name) =>
        Attributes.ValueKind == JsonValueKind.Object && Attributes.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetDouble()).ToArray() : null;

    /// <summary>A sensor's value with its unit ("21.5 °C"); a state as words otherwise.</summary>
    public string ValueText()
    {
        if (Unavailable) return T("Unavailable");
        if (State == "unknown") return T("Unknown");
        string unit = Text("unit_of_measurement") ?? "";
        if (double.TryParse(State, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
        {
            string n = Math.Abs(d) >= 100 || d == Math.Round(d) ? d.ToString("0.#") : d.ToString("0.##");
            return unit.Length == 0 ? n : unit == "%" ? n + "%" : $"{n} {unit}";
        }
        if (Domain == "binary_sensor") return Domains.BinaryText(Text("device_class"), State == "on");
        return Domains.StateWords(State);
    }
}

public sealed record HaArea(string Id, string Name, string? FloorId);
public sealed record HaFloor(string Id, string Name, int Level);
public sealed record HaDevice(string Id, string Name, string? AreaId, string Manufacturer, string Model, bool Service);

/// <summary>What the entity registry says of an entity (absent: it has no unique id, so it's on its own).</summary>
public sealed record HaRegistryEntry(string EntityId, string? DeviceId, string? AreaId, bool Hidden, bool Category, string? Name, string? OriginalName);

/// <summary>
/// One box on the grid: a device (with its entities) or an entity that has no device (a helper, a scene, an
/// automation). <see cref="Shown"/> are the entities the box lists; <see cref="All"/> what its window shows.
/// </summary>
public sealed class HaThing
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public string? DeviceId { get; init; }
    public string? AreaId { get; set; }
    public string About { get; init; } = "";
    public List<string> Shown { get; } = new();
    public List<string> All { get; } = new();
    /// <summary>The entity its quick control is for (the most important one that can be switched or set), or null.</summary>
    public string? Main { get; set; }
    /// <summary>Its kind, from <see cref="Main"/> (or its first entity): light, switch, sensor…</summary>
    public string Type { get; set; } = "";
}

/// <summary>What the app knows of each kind of entity: its name, what can be done with it, and its level.</summary>
public static class Domains
{
    /// <summary>Not shown on the grid: nothing to see or do there.</summary>
    public static readonly HashSet<string> Skipped = new()
    {
        "zone", "update", "event", "tts", "stt", "conversation", "tag", "persistent_notification", "wake_word",
        "assist_satellite", "ai_task", "image", "todo", "calendar", "notify", "sun", "date", "time", "datetime", "text",
        "input_text", "input_datetime", "counter", "timer", "schedule", "group", "image_processing", "geo_location",
    };

    /// <summary>The order a device's entities count as its main one (anything switchable before anything read).</summary>
    private static readonly string[] Priority =
    {
        "light", "climate", "fan", "cover", "media_player", "lock", "vacuum", "lawn_mower", "valve", "humidifier",
        "water_heater", "switch", "siren", "remote", "input_boolean", "number", "input_number", "select", "input_select",
        "alarm_control_panel", "scene", "script", "automation", "button", "input_button",
    };

    public static int Rank(string domain) { int i = Array.IndexOf(Priority, domain); return i < 0 ? 1000 : i; }

    public static bool Controllable(string domain) => Rank(domain) < 1000;

    /// <summary>The kind's name, as a group ("Lights").</summary>
    public static string TypeName(string domain) => domain switch
    {
        "light" => T("Lights"),
        "switch" => T("Switches"),
        "fan" => T("Fans"),
        "cover" => T("Covers"),
        "climate" or "water_heater" => T("Climate"),
        "media_player" or "remote" => T("Media"),
        "lock" => T("Locks"),
        "vacuum" or "lawn_mower" => T("Robots"),
        "valve" => T("Valves"),
        "humidifier" => T("Humidifiers"),
        "siren" or "alarm_control_panel" => T("Security"),
        "input_boolean" or "input_number" or "input_select" or "input_button" => T("Helpers"),
        "number" or "select" or "button" => T("Controls"),
        "scene" => T("Scenes"),
        "script" => T("Scripts"),
        "automation" => T("Automations"),
        "sensor" or "binary_sensor" => T("Sensors"),
        "person" or "device_tracker" => T("People"),
        "weather" => T("Weather"),
        "camera" => T("Cameras"),
        _ => T("Other"),
    };

    /// <summary>The kind's name for one entity ("Light").</summary>
    public static string OneName(string domain) => domain switch
    {
        "light" => T("Light"), "switch" => T("Switch"), "fan" => T("Fan"), "cover" => T("Cover"), "climate" => T("Climate"),
        "media_player" => T("Media player"), "lock" => T("Lock"), "vacuum" => T("Vacuum"), "valve" => T("Valve"),
        "humidifier" => T("Humidifier"), "siren" => T("Siren"), "scene" => T("Scene"), "script" => T("Script"),
        "automation" => T("Automation"), "button" or "input_button" => T("Button"), "sensor" => T("Sensor"),
        "binary_sensor" => T("Sensor"), _ => domain.Replace('_', ' '),
    };

    public static string StateWords(string state) => state switch
    {
        "on" => T("On"), "off" => T("Off"), "open" => T("Open"), "closed" => T("Closed"), "opening" => T("Opening"),
        "closing" => T("Closing"), "locked" => T("Locked"), "unlocked" => T("Unlocked"), "locking" => T("Locking"),
        "unlocking" => T("Unlocking"), "jammed" => T("Jammed"), "playing" => T("Playing"), "paused" => T("Paused"),
        "idle" => T("Idle"), "standby" => T("Standby"), "cleaning" => T("Cleaning"), "docked" => T("Docked"),
        "returning" => T("Returning"), "mowing" => T("Mowing"), "home" => T("Home"), "not_home" => T("Away"),
        "heat" => T("Heat"), "cool" => T("Cool"), "heat_cool" => T("Heat / cool"), "auto" => T("Auto"), "dry" => T("Dry"),
        "fan_only" => T("Fan only"), "unavailable" => T("Unavailable"), "unknown" => T("Unknown"),
        "disarmed" => T("Disarmed"), "armed_home" => T("Armed home"), "armed_away" => T("Armed away"), "armed_night" => T("Armed night"),
        "triggered" => T("Triggered"),
        _ => state.Length > 0 ? char.ToUpper(state[0]) + state[1..].Replace('_', ' ') : "",
    };

    public static string BinaryText(string? deviceClass, bool on) => deviceClass switch
    {
        "door" or "window" or "garage_door" or "opening" => on ? T("Open") : T("Closed"),
        "motion" or "occupancy" or "presence" => on ? T("Detected") : T("Clear"),
        "moisture" => on ? T("Wet") : T("Dry"),
        "lock" => on ? T("Unlocked") : T("Locked"),
        "connectivity" => on ? T("Connected") : T("Disconnected"),
        "plug" => on ? T("Plugged in") : T("Unplugged"),
        "battery" => on ? T("Low") : T("Normal"),
        "problem" => on ? T("Problem") : T("OK"),
        _ => on ? T("On") : T("Off"),
    };

    // ---------------------------------------------------------------- what can be done

    /// <summary>Switched on and off (the quick toggle, and a shortcut).</summary>
    public static bool CanToggle(HaEntity e) => e.Domain switch
    {
        "light" or "switch" or "fan" or "input_boolean" or "automation" or "siren" or "humidifier" or "remote" or "lock" => true,
        "media_player" => e.Has(128) || e.Has(256) || e.Has(1) || e.Has(16384),
        "cover" => e.Has(1) || e.Has(2),
        "valve" => e.Has(1) || e.Has(2),
        "climate" => e.List("hvac_modes").Contains("off"),
        "vacuum" or "lawn_mower" => true,
        _ => false,
    };

    /// <summary>Run with one press (a scene, a script, a button).</summary>
    public static bool IsPress(string domain) => domain is "scene" or "script" or "button" or "input_button";

    public static string PressWord(string domain) => domain switch
    {
        "scene" => T("Activate"), "script" => T("Run"), _ => T("Press"),
    };

    /// <summary>Whether it counts as on (lit, open, unlocked, playing, heating, cleaning).</summary>
    public static bool IsOn(HaEntity e) => e.Domain switch
    {
        "cover" or "valve" => e.State is "open" or "opening",
        "lock" => e.State is "unlocked" or "unlocking" or "open",
        "media_player" => e.State is not ("off" or "standby" or "unavailable" or "unknown"),
        "climate" => e.State is not ("off" or "unavailable" or "unknown"),
        "vacuum" => e.State is "cleaning" or "returning",
        "lawn_mower" => e.State is "mowing",
        "script" => e.State == "on",
        _ => e.State == "on",
    };

    /// <summary>Has an amount that can be set: brightness, speed, position, volume, target temperature, a number.</summary>
    public static Level? LevelOf(HaEntity e)
    {
        switch (e.Domain)
        {
            case "light":
                var modes = e.List("supported_color_modes");
                if (modes.Count == 0 || modes.All(m => m == "onoff")) return null;
                double b = e.State == "on" ? (e.Number("brightness") ?? 255) / 255.0 * 100 : 0;
                return new Level(Math.Round(b), 0, 100, 10, "%", T("Brightness"));
            case "fan":
                if (!e.Has(1)) return null;
                return new Level(e.State == "on" ? e.Number("percentage") ?? 0 : 0, 0, 100, e.Number("percentage_step") is { } fs and > 0 and < 100 ? fs : 10, "%", T("Speed"));
            case "cover":
                if (!e.Has(4)) return null;
                return new Level(e.Number("current_position") ?? 0, 0, 100, 10, "%", T("Position"));
            case "valve":
                if (!e.Has(4)) return null;
                return new Level(e.Number("current_position") ?? 0, 0, 100, 10, "%", T("Position"));
            case "media_player":
                if (!e.Has(4)) return null;
                return new Level(Math.Round((e.Number("volume_level") ?? 0) * 100), 0, 100, 5, "%", T("Volume"));
            case "humidifier":
                return new Level(e.Number("humidity") ?? 50, e.Number("min_humidity") ?? 0, e.Number("max_humidity") ?? 100, 1, "%", T("Humidity"));
            case "climate":
            case "water_heater":
                if (e.Number("temperature") is not { } t) return null;
                string unit = e.Text("temperature_unit") ?? (t > 45 ? "°F" : "°C");
                double step = e.Number("target_temp_step") ?? (unit == "°F" ? 1 : 0.5);
                return new Level(t, e.Number("min_temp") ?? 7, e.Number("max_temp") ?? 35, step, unit, T("Target"));
            case "number":
            case "input_number":
                if (!double.TryParse(e.State, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) v = e.Number("min") ?? 0;
                return new Level(v, e.Number("min") ?? 0, e.Number("max") ?? 100, e.Number("step") is { } s and > 0 ? s : 1, e.Text("unit_of_measurement") ?? "", T("Value"));
        }
        return null;
    }

    /// <summary>The service call that sets a level (and its data).</summary>
    public static (string Service, Dictionary<string, object?> Data) SetLevel(HaEntity e, double v) => e.Domain switch
    {
        "light" => v <= 0 ? ("turn_off", new()) : ("turn_on", new() { ["brightness_pct"] = (int)Math.Round(v) }),
        "fan" => ("set_percentage", new() { ["percentage"] = (int)Math.Round(v) }),
        "cover" => ("set_cover_position", new() { ["position"] = (int)Math.Round(v) }),
        "valve" => ("set_valve_position", new() { ["position"] = (int)Math.Round(v) }),
        "media_player" => ("volume_set", new() { ["volume_level"] = Math.Round(v) / 100.0 }),
        "humidifier" => ("set_humidity", new() { ["humidity"] = (int)Math.Round(v) }),
        "climate" or "water_heater" => ("set_temperature", new() { ["temperature"] = v }),
        _ => ("set_value", new() { ["value"] = v }),
    };

    /// <summary>The service call that switches it the other way.</summary>
    public static (string Service, Dictionary<string, object?> Data) Toggle(HaEntity e)
    {
        bool on = IsOn(e);
        return e.Domain switch
        {
            "lock" => (on ? "lock" : "unlock", new()),
            "cover" => (on ? "close_cover" : "open_cover", new()),
            "valve" => (on ? "close_valve" : "open_valve", new()),
            "climate" => (on ? "turn_off" : "turn_on", new()),
            "vacuum" => (on ? "return_to_base" : "start", new()),
            "lawn_mower" => (on ? "dock" : "start_mowing", new()),
            "media_player" => (on ? (e.Has(256) ? "turn_off" : "media_pause") : (e.Has(128) ? "turn_on" : "media_play"), new()),
            _ => (on ? "turn_off" : "turn_on", new()),
        };
    }

    public static string PressService(string domain) => domain switch
    {
        "button" or "input_button" => "press",
        _ => "turn_on",
    };

    /// <summary>A short line of what it's doing ("On · 70%", "21.5 °C → 22 °C", "Playing: title").</summary>
    public static string Summary(HaEntity e)
    {
        if (e.Unavailable) return e.ValueText();
        var level = LevelOf(e);
        switch (e.Domain)
        {
            case "light" or "fan" when IsOn(e) && level != null:
                return $"{T("On")} · {level.Value:0}%";
            case "cover" or "valve" when level != null && e.State is "open" or "opening" or "closing":
                return $"{StateWords(e.State)} · {level.Value:0}%";
            case "climate":
                string now = e.Number("current_temperature") is { } c ? $"{c:0.#}°" : "";
                string target = e.Number("temperature") is { } t ? $"{t:0.#}°" : e.Number("target_temp_low") is { } lo && e.Number("target_temp_high") is { } hi ? $"{lo:0.#}–{hi:0.#}°" : "";
                string action = e.Text("hvac_action") is { } a ? StateWords(a) : StateWords(e.State);
                return string.Join(" · ", new[] { action, now.Length > 0 && target.Length > 0 && e.State != "off" ? $"{now} → {target}" : now }.Where(x => x.Length > 0));
            case "media_player":
                string title = e.Text("media_title") ?? "";
                string artist = e.Text("media_artist") ?? "";
                string playing = title.Length > 0 ? (artist.Length > 0 ? $"{artist} – {title}" : title) : "";
                return playing.Length > 0 && e.State is "playing" or "paused" ? $"{StateWords(e.State)}: {playing}" : StateWords(e.State);
            case "scene":
            case "button":
            case "input_button":
                return e.LastChanged > DateTime.MinValue && e.State is not ("unknown" or "unavailable") ? F("Last {0:g}", e.LastChanged) : "";
            case "script":
                return e.State == "on" ? T("Running") : "";
            case "automation":
                return e.State == "on" ? T("On") : T("Off");
            case "number" or "input_number" or "select" or "input_select" or "sensor" or "binary_sensor":
                return e.ValueText();
        }
        return StateWords(e.State);
    }
}

/// <summary>An amount and its range: brightness 0–100 %, a target temperature 7–35 °C in steps of 0.5…</summary>
public sealed record Level(double Value, double Min, double Max, double Step, string Unit, string Name)
{
    public string Show(double v) => (Step < 1 ? v.ToString("0.#") : v.ToString("0")) + (Unit == "%" ? "%" : Unit.Length > 0 ? " " + Unit : "");
}
