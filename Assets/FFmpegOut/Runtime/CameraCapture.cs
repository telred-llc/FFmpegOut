// FFmpegOut - FFmpeg video encoding plugin for Unity
// https://github.com/keijiro/KlakNDI

using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using UniRx;

namespace FFmpegOut
{
    [AddComponentMenu("FFmpegOut/Camera Capture")]
    public sealed class CameraCapture : MonoBehaviour
    {
        #region Public properties

        [SerializeField] private Camera camera;
        private bool _readyToPush = false;
        
        [SerializeField] int _width = 1920;

        public int width
        {
            get { return _width; }
            set { _width = value; }
        }

        [SerializeField] int _height = 1080;

        public int height
        {
            get { return _height; }
            set { _height = value; }
        }

        [SerializeField] FFmpegPreset _preset;

        public FFmpegPreset preset
        {
            get { return _preset; }
            set { _preset = value; }
        }

        [SerializeField] float _frameRate = 60;

        public float frameRate
        {
            get { return _frameRate; }
            set { _frameRate = value; }
        }

        [SerializeField] bool _recordAudio = false;

        public bool recordAudio
        {
            get { return _recordAudio; }
            set { _recordAudio = value; }
        }
#if FFMPEG_OUT_CUSTOM_FILE_NAME
        [SerializeField] string _fileName = "";

        public string fileName
        {
            get { return _fileName; }
            set { _fileName = value; }
        }
#endif

        #endregion

        #region Private members

        FFmpegSession _session;
        RenderTexture _tempRT;
        GameObject _blitter;

        RenderTextureFormat GetTargetFormat(Camera camera)
        {
            return camera.allowHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default;
        }

        int GetAntiAliasingLevel(Camera camera)
        {
            return camera.allowMSAA ? QualitySettings.antiAliasing : 1;
        }

        #endregion

        #region Time-keeping variables

        int _frameCount;
        float _startTime;
        int _frameDropCount;

        float FrameTime
        {
            get { return _startTime + (_frameCount - 0.5f) / _frameRate; }
        }

        void WarnFrameDrop()
        {
            if (++_frameDropCount != 10) return;

            Debug.LogWarning(
                "Significant frame droppping was detected. This may introduce " +
                "time instability into output video. Decreasing the recording " +
                "frame rate is recommended."
            );
        }

        #endregion

        #region MonoBehaviour implementation

        void OnValidate()
        {
            _width = Mathf.Max(8, _width);
            _height = Mathf.Max(8, _height);
        }

        void OnDisable()
        {
            if (_session != null)
            {
                // Close and dispose the FFmpeg session.
                _session.Close();
                _session.Dispose();
                _session = null;
            }

            if (_tempRT != null)
            {
                // Dispose the frame texture.
                GetComponent<Camera>().targetTexture = null;
                Destroy(_tempRT);
                _tempRT = null;
            }

            if (_blitter != null)
            {
                // Destroy the blitter game object.
                Destroy(_blitter);
                _blitter = null;
            }
        }

        IEnumerator Start()
        {
            // Sync with FFmpeg pipe thread at the end of every frame.
            for (var eof = new WaitForEndOfFrame();;)
            {
                yield return eof;
                _session?.CompletePushFrames();
            }
        }

        void OnAudioFilterRead(float[] buffer, int channels)
        {
            if (_session == null || !_session.recordAudio || !_readyToPush) return;
            _session.PushAudioBuffer(buffer, channels);
        }

        private void Awake()
        {
            // var camera = GetComponent<Camera>();
            camera = this.GetComponent<Camera>();
            Observable.Timer(TimeSpan.FromSeconds(1)).ObserveOn(Scheduler.MainThread).SubscribeOn(Scheduler.MainThread).Subscribe(l =>
            {
                Debug.Log("Ready To Push");
                _readyToPush  = true;
            });

            // Lazy initialization
            if (_session == null)
            {
                // Give a newly created temporary render texture to the camera
                // if it's set to render to a screen. Also create a blitter
                // object to keep frames presented on the screen.
                if (camera.targetTexture == null)
                {
                    _tempRT = new RenderTexture(_width, _height, 24, GetTargetFormat(camera));
                    _tempRT.antiAliasing = GetAntiAliasingLevel(camera);
                    camera.targetTexture = _tempRT;
                    _blitter = Blitter.CreateInstance(camera);
                }

                // Start an FFmpeg session.
                _session = FFmpegSession.Create(
#if FFMPEG_OUT_CUSTOM_FILE_NAME
                    _fileName,
#else
                    gameObject.name,
#endif
                    camera.targetTexture.width,
                    camera.targetTexture.height,
                    _frameRate, preset, recordAudio, GetAvailablePort(50505)
                );

                _startTime = Time.time;
                _frameCount = 0;
                _frameDropCount = 0;
            }
        }

        void Update()
        {
//             var camera = GetComponent<Camera>();
//
//             // Lazy initialization
//             if (_session == null)
//             {
//                 // Give a newly created temporary render texture to the camera
//                 // if it's set to render to a screen. Also create a blitter
//                 // object to keep frames presented on the screen.
//                 if (camera.targetTexture == null)
//                 {
//                     _tempRT = new RenderTexture(_width, _height, 24, GetTargetFormat(camera));
//                     _tempRT.antiAliasing = GetAntiAliasingLevel(camera);
//                     camera.targetTexture = _tempRT;
//                     _blitter = Blitter.CreateInstance(camera);
//                 }
//
//                 // Start an FFmpeg session.
//                 _session = FFmpegSession.Create(
// #if FFMPEG_OUT_CUSTOM_FILE_NAME
//                     _fileName,
// #else
//                     gameObject.name,
// #endif
//                     camera.targetTexture.width,
//                     camera.targetTexture.height,
//                     _frameRate, preset, recordAudio, GetAvailablePort(50505)
//                 );
//
//                 _startTime = Time.time;
//                 _frameCount = 0;
//                 _frameDropCount = 0;
//             }

            if (!_readyToPush)
            {
                return;
            }

            var gap = Time.time - FrameTime;
            var delta = 1 / _frameRate;

            if (gap < 0)
            {
                // Update without frame data.
                _session.PushFrame(null);
            }
            else if (gap < delta)
            {
                // Single-frame behind from the current time:
                // Push the current frame to FFmpeg.
                _session.PushFrame(camera.targetTexture);
                _frameCount++;
            }
            else if (gap < delta * 2)
            {
                // Two-frame behind from the current time:
                // Push the current frame twice to FFmpeg. Actually this is not
                // an efficient way to catch up. We should think about
                // implementing frame duplication in a more proper way. #fixme
                _session.PushFrame(camera.targetTexture);
                _session.PushFrame(camera.targetTexture);
                _frameCount += 2;
            }
            else
            {
                // Show a warning message about the situation.
                WarnFrameDrop();

                // Push the current frame to FFmpeg.
                _session.PushFrame(camera.targetTexture);

                // Compensate the time delay.
                _frameCount += Mathf.FloorToInt(gap * _frameRate);
            }
        }

        #endregion
        
        public int GetAvailablePort(int startingPort)
        {
            var portArray = new List<int>();

            var properties = IPGlobalProperties.GetIPGlobalProperties();

            // Ignore active connections
            var connections = properties.GetActiveTcpConnections();
            portArray.AddRange(from n in connections
                where n.LocalEndPoint.Port >= startingPort
                select n.LocalEndPoint.Port);

            // Ignore active tcp listners
            var endPoints = properties.GetActiveTcpListeners();
            portArray.AddRange(from n in endPoints
                where n.Port >= startingPort
                select n.Port);

            // Ignore active UDP listeners
            endPoints = properties.GetActiveUdpListeners();
            portArray.AddRange(from n in endPoints
                where n.Port >= startingPort
                select n.Port);

            portArray.Sort();

            for (var i = startingPort; i < UInt16.MaxValue; i++)
                if (!portArray.Contains(i))
                    return i;

            return 0;
        }
    }
}