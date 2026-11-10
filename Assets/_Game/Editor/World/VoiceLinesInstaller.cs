using System.Collections.Generic;
using System.Text;
using ChronoWild.Story;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ChronoWild.Editor.World
{
    /// <summary>
    /// Hooks the protagonist's recorded journal lines (Art/Story/Voice/VO_*.wav) up to the
    /// mission in the open scene. Lines that are missing are listed and simply stay silent.
    /// </summary>
    public static class VoiceLinesInstaller
    {
        const string VoiceDir = "Assets/_Game/Art/Story/Voice";

        // Part display name -> clip file name.
        static readonly Dictionary<string, string> PartClips = new Dictionary<string, string>
        {
            { "Caja de suministros", "VO_SupplyCrate" },
            { "Célula de energía", "VO_PowerCell" },
            { "Botiquín", "VO_MedicalKit" },
            { "Módulo de navegación", "VO_NavigationModule" },
            { "Estabilizador temporal", "VO_TemporalStabilizer" },
            { "Núcleo cronal", "VO_ChronoCore" },
        };

        [MenuItem("CHRONO WILD/World/Assign Voice Lines")]
        public static void Assign()
        {
            var mission = Object.FindFirstObjectByType<MissionDirector>(FindObjectsInactive.Include);
            if (mission == null)
            {
                Debug.LogError("Assign Voice Lines: no MissionDirector in the open scene (run Install Story first).");
                return;
            }
            var missing = new List<string>();
            int found = 0;

            AudioClip Load(string id)
            {
                string path = VoiceDir + "/" + id + ".wav";
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (importer == null)
                {
                    missing.Add(id);
                    return null;
                }
                // Speech: mono, Vorbis, small in memory and in the web build.
                bool dirty = !importer.forceToMono;
                importer.forceToMono = true;
                var settings = importer.defaultSampleSettings;
                if (settings.compressionFormat != AudioCompressionFormat.Vorbis || settings.loadType != AudioClipLoadType.CompressedInMemory)
                {
                    settings.compressionFormat = AudioCompressionFormat.Vorbis;
                    settings.quality = 0.6f;
                    settings.loadType = AudioClipLoadType.CompressedInMemory;
                    importer.defaultSampleSettings = settings;
                    dirty = true;
                }
                if (dirty)
                    importer.SaveAndReimport();
                found++;
                return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            }

            Undo.RecordObject(mission, "Assign Voice Lines");
            mission.introVoice = Load("VO_Day1");
            mission.finalVoice = Load("VO_Final");
            EditorUtility.SetDirty(mission);
            foreach (var part in mission.parts)
            {
                if (part == null)
                    continue;
                Undo.RecordObject(part, "Assign Voice Lines");
                part.voiceLine = PartClips.TryGetValue(part.displayName, out var id) ? Load(id) : null;
                EditorUtility.SetDirty(part);
            }
            EditorSceneManager.MarkSceneDirty(mission.gameObject.scene);
            EditorSceneManager.SaveScene(mission.gameObject.scene);

            var report = new StringBuilder($"Assign Voice Lines: {found} of {found + missing.Count} lines assigned.");
            if (missing.Count > 0)
                report.Append(" Missing in " + VoiceDir + ": " + string.Join(", ", missing));
            Debug.Log(report.ToString());
        }
    }
}
