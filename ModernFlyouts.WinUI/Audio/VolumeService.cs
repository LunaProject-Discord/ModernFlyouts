using System;
using NAudio.CoreAudioApi;

namespace ModernFlyouts.WinUI.Audio
{
    /// <summary>
    /// Default render endpoint volume. Notifications arrive on a COM thread;
    /// subscribers must marshal to the UI thread themselves.
    /// </summary>
    public sealed class VolumeService : IDisposable
    {
        private readonly MMDeviceEnumerator _enumerator = new();
        private MMDevice? _device;

        /// <summary>(volume 0-100, isMuted)</summary>
        public event Action<double, bool>? VolumeChanged;

        public VolumeService()
        {
            try
            {
                _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _device.AudioEndpointVolume.OnVolumeNotification += OnVolumeNotification;
            }
            catch
            {
                _device = null;
            }
        }

        public bool HasDevice => _device != null;

        public double Volume
        {
            get => _device == null ? 0 : Math.Round(_device.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
            set
            {
                if (_device == null) return;
                _device.AudioEndpointVolume.MasterVolumeLevelScalar = (float)Math.Clamp(value / 100.0, 0, 1);
            }
        }

        public bool IsMuted
        {
            get => _device?.AudioEndpointVolume.Mute ?? false;
            set
            {
                if (_device != null) _device.AudioEndpointVolume.Mute = value;
            }
        }

        private long _lastChangeTick;

        private void OnVolumeNotification(AudioVolumeNotificationData data)
        {
            System.Threading.Interlocked.Exchange(ref _lastChangeTick, Environment.TickCount64);
            VolumeChanged?.Invoke(Math.Round(data.MasterVolume * 100), data.Muted);
        }

        /// <summary>Whether the volume or mute state changed within the given period.</summary>
        public bool HasRecentChange(TimeSpan within)
        {
            long last = System.Threading.Interlocked.Read(ref _lastChangeTick);
            return last != 0 && Environment.TickCount64 - last <= within.TotalMilliseconds;
        }

        public void Dispose()
        {
            if (_device != null)
            {
                _device.AudioEndpointVolume.OnVolumeNotification -= OnVolumeNotification;
                _device.Dispose();
            }
            _enumerator.Dispose();
        }
    }
}
