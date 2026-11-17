using System;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace ModernFlyouts.WinUI.Audio
{
    /// <summary>
    /// Volume of the default output device. Follows the default device when it changes
    /// (e.g. Bluetooth earbuds connect), like the WPF version did.
    /// Notifications arrive on a COM thread; subscribers must marshal to the UI thread themselves.
    /// </summary>
    public sealed class VolumeService : IDisposable, IMMNotificationClient
    {
        private readonly MMDeviceEnumerator _enumerator = new();
        private readonly object _gate = new();
        private MMDevice? _device;

        /// <summary>(volume 0-100, isMuted)</summary>
        public event Action<double, bool>? VolumeChanged;

        public VolumeService()
        {
            AttachDefaultDevice();
            try
            {
                _enumerator.RegisterEndpointNotificationCallback(this);
            }
            catch (Exception ex)
            {
                App.Log($"audio device notifications unavailable: {ex.Message}");
            }
        }

        public bool HasDevice => _device != null;

        public double Volume
        {
            // Under the lock: the default device may be swapped (and disposed) on another thread.
            get
            {
                lock (_gate)
                {
                    try { return _device == null ? 0 : Math.Round(_device.AudioEndpointVolume.MasterVolumeLevelScalar * 100); }
                    catch { return 0; }
                }
            }
            set
            {
                lock (_gate)
                {
                    try
                    {
                        if (_device != null) _device.AudioEndpointVolume.MasterVolumeLevelScalar = (float)Math.Clamp(value / 100.0, 0, 1);
                    }
                    catch { }
                }
            }
        }

        public bool IsMuted
        {
            get
            {
                lock (_gate)
                {
                    try { return _device?.AudioEndpointVolume.Mute ?? false; }
                    catch { return false; }
                }
            }
            set
            {
                lock (_gate)
                {
                    try
                    {
                        if (_device != null) _device.AudioEndpointVolume.Mute = value;
                    }
                    catch { }
                }
            }
        }

        private void AttachDefaultDevice()
        {
            lock (_gate)
            {
                DetachDevice();
                try
                {
                    _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    _device.AudioEndpointVolume.OnVolumeNotification += OnVolumeNotification;
                    App.Log($"audio device: {_device.FriendlyName}");
                }
                catch (Exception ex)
                {
                    _device = null;
                    App.Log($"no audio device: {ex.Message}");
                }
            }

            // The new device has its own level: update the flyout right away.
            VolumeChanged?.Invoke(Volume, IsMuted);
        }

        private void DetachDevice()
        {
            if (_device == null)
            {
                return;
            }

            try
            {
                _device.AudioEndpointVolume.OnVolumeNotification -= OnVolumeNotification;
                _device.Dispose();
            }
            catch { }
            _device = null;
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

        #region IMMNotificationClient

        void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow != DataFlow.Render || role != Role.Multimedia)
            {
                return;
            }

            // Called on a COM thread that must not re-enter the device enumerator: switch later.
            System.Threading.ThreadPool.QueueUserWorkItem(_ => AttachDefaultDevice());
        }

        void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState) { }

        void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId) { }

        void IMMNotificationClient.OnDeviceRemoved(string deviceId) { }

        void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

        #endregion

        public void Dispose()
        {
            try { _enumerator.UnregisterEndpointNotificationCallback(this); } catch { }
            lock (_gate)
            {
                DetachDevice();
            }
            _enumerator.Dispose();
        }
    }
}
