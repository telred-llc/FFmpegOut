// FFmpegOut - FFmpeg video encoding plugin for Unity
// https://github.com/keijiro/KlakNDI

using UnityEngine;
using UnityEditor;
using System.Linq;

namespace FFmpegOut
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(CameraCapture))]
    public class CameraCaptureEditor : Editor
    {
        SerializedProperty _width;
        SerializedProperty _height;
        SerializedProperty _preset;
        SerializedProperty _frameRate;
        SerializedProperty _recordAudio;
        SerializedProperty _audioPort;
#if FFMPEG_OUT_CUSTOM_FILE_NAME
        private SerializedProperty _fileName;
#endif
        GUIContent[] _presetLabels;
        int[] _presetOptions;

        // It shows the render format options when:
        // - Editing multiple objects.
        // - No target texture is specified in the camera.
        bool ShouldShowFormatOptions
        {
            get {
                if (targets.Length > 1) return true;
                var camera = ((Component)target).GetComponent<Camera>();
                return camera.targetTexture == null;
            }
        }

        void OnEnable()
        {
            _width = serializedObject.FindProperty("_width");
            _height = serializedObject.FindProperty("_height");
            _preset = serializedObject.FindProperty("_preset");
            _frameRate = serializedObject.FindProperty("_frameRate");
            _recordAudio = serializedObject.FindProperty("_recordAudio");
            _audioPort = serializedObject.FindProperty("_audioPort");
#if FFMPEG_OUT_CUSTOM_FILE_NAME
            _fileName = serializedObject.FindProperty("_fileName");
#endif

            var presets = FFmpegPreset.GetValues(typeof(FFmpegPreset));
            _presetLabels = presets.Cast<FFmpegPreset>().
                Select(p => new GUIContent(p.GetDisplayName())).ToArray();
            _presetOptions = presets.Cast<int>().ToArray();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (ShouldShowFormatOptions)
            {
                EditorGUILayout.PropertyField(_width);
                EditorGUILayout.PropertyField(_height);
            }

            EditorGUILayout.IntPopup(_preset, _presetLabels, _presetOptions);
            EditorGUILayout.PropertyField(_frameRate);
            EditorGUILayout.PropertyField(_recordAudio);
            EditorGUILayout.PropertyField(_audioPort);
#if FFMPEG_OUT_CUSTOM_FILE_NAME
            EditorGUILayout.PropertyField(_fileName);
#endif
            serializedObject.ApplyModifiedProperties();
        }
    }
}
