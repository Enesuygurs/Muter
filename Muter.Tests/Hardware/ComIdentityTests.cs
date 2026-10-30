using System;
using System.Runtime.InteropServices;
using Muter.Audio;
using Xunit;

namespace Muter.Tests.Hardware
{
    // CoreAudioEndpointProvider releases every device and volume object right after use. That is only safe because
    // Windows hands out a new object (and so a new runtime wrapper) for each call - verified here. The enumerator,
    // on the other hand, is a shared singleton and must not be released on dispose.
    [Collection("Hardware")]
    public class ComIdentityTests
    {
        public ComIdentityTests(HardwareFixture hardware) => TestSetup.Init();

        [Fact]
        public void Device_and_volume_objects_are_never_shared_but_the_enumerator_is()
        {
            IMMDeviceEnumerator first = CoreAudioInterop.CreateDeviceEnumerator();
            IMMDeviceEnumerator second = CoreAudioInterop.CreateDeviceEnumerator();
            Assert.Same(first, second);

            Assert.Equal(0, first.GetDefaultAudioEndpoint(EDataFlow.Capture, ERole.Console, out IMMDevice defaultDevice));
            Assert.Equal(0, defaultDevice.GetId(out IntPtr idPointer));
            string id = Marshal.PtrToStringUni(idPointer);
            Marshal.FreeCoTaskMem(idPointer);

            Assert.Equal(0, first.GetDevice(id, out IMMDevice device1));
            Assert.Equal(0, first.GetDevice(id, out IMMDevice device2));
            Assert.NotSame(device1, device2);
            Assert.NotSame(defaultDevice, device1);

            Guid iid = CoreAudioInterop.IID_IAudioEndpointVolume;
            Assert.Equal(0, device1.Activate(ref iid, CoreAudioInterop.CLSCTX_ALL, IntPtr.Zero, out object volume1));
            Assert.Equal(0, device1.Activate(ref iid, CoreAudioInterop.CLSCTX_ALL, IntPtr.Zero, out object volume2));
            Assert.Equal(0, device2.Activate(ref iid, CoreAudioInterop.CLSCTX_ALL, IntPtr.Zero, out object volume3));
            Assert.NotSame(volume1, volume2);
            Assert.NotSame(volume1, volume3);

            foreach (object o in new[] { volume1, volume2, volume3, device1, device2, defaultDevice })
                Marshal.ReleaseComObject(o);
        }
    }
}
