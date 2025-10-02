using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;


// This response object maps to the JSON from the server's version endpoint.

[JsonObject(MemberSerialization.OptIn)]
public class UltiPawVersionResponse
{
#if UNITY_EDITOR
    [JsonProperty] public string recommendedVersion;
    [JsonProperty] public List<UltiPawVersion> versions;
#endif
}

// Represents a single available version of an UltiPaw modification.
[JsonObject(MemberSerialization.OptIn)]
#if UNITY_EDITOR
public class UltiPawVersion : IEquatable<UltiPawVersion>
#else
public class UltiPawVersion
#endif
{
    [JsonProperty] public string version;
    [JsonProperty] public string defaultAviVersion;
    [JsonProperty] public Scope scope;
    [JsonProperty] public string date;
    [JsonProperty] public string changelog;
    [JsonProperty] public string customAviHash;
    [JsonProperty] public string appliedCustomAviHash;
    [JsonProperty] public string[] defaultAviHash;
    [JsonProperty] public string[] customBlendshapes;
    [JsonProperty] public string[] extraCustomization;
    [JsonProperty] public Dictionary<string, string> dependencies;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public int uploaderId;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string parentVersion;
    
    // --- Fields for local unsubmitted versions ---
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string baseFbxHash;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string customFbxPath;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string ultipawAvatarPath;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string logicPrefabPath;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public bool? includeCustomVeins;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string customVeinsTexturePath;

    [JsonIgnore] public bool isUnsubmitted; // Runtime flag, not saved to JSON

    public bool Equals(UltiPawVersion other)
    {
        if (other == null) return false;
        // Two versions are the same if their version string and base FBX version match.
        return version == other.version && defaultAviVersion == other.defaultAviVersion;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(version, defaultAviVersion);
    }

    public override bool Equals(object obj)
    {
        return Equals(obj as UltiPawVersion);
    }
}

// Defines the release scope of a version (e.g., public, beta).
[JsonConverter(typeof(StringEnumConverter))]
public enum Scope
{
#if UNITY_EDITOR
    [EnumMember(Value = "public")] PUBLIC,
    [EnumMember(Value = "beta")] BETA,
    [EnumMember(Value = "alpha")] ALPHA,
    [EnumMember(Value = "unknown")] UNKNOWN
#endif
}
