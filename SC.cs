using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WindowsServiceUtils;

// ReSharper disable once InconsistentNaming
internal static class SC {
	public static void Create(
		string name,
		string binPath,
		ServiceStartType startType,
		string? displayName = null,
		string[]? dependencies = null,
		string? username = null,
		string? password = null
	) {
		var scManager = OpenSCManager(null, null, SC_MANAGER_CREATE_SERVICE);
		if (scManager == IntPtr.Zero)
			ThrowLastWin32Error();
		try {
			var deps = MarshalDependencies(dependencies);
			try {
				uint nativeStartType = ToNativeStartType(startType);
				var service = CreateService(
					scManager,
					name,
					displayName,
					SERVICE_ALL_ACCESS,
					SERVICE_WIN32_OWN_PROCESS,
					nativeStartType,
					SERVICE_ERROR_NORMAL,
					binPath,
					null,
					IntPtr.Zero,
					deps,
					username,
					password
				);
				if (service == IntPtr.Zero)
					ThrowLastWin32Error();
				try {
					if (startType == ServiceStartType.DelayedAuto) {
						var info = new ServiceDelayedAutoStartInfo { fDelayedAutostart = true };
						if (!ChangeServiceConfig2(service, SERVICE_CONFIG_DELAYED_AUTO_START_INFO, ref info))
							ThrowLastWin32Error();
					}
				}
				finally {
					CloseServiceHandle(service);
				}
			}
			finally {
				if (deps != IntPtr.Zero)
					Marshal.FreeHGlobal(deps);
			}
		}
		finally {
			CloseServiceHandle(scManager);
		}
	}

	public static void SetDescription(string name, string description) {
		var scManager = OpenSCManager(null, null, SC_MANAGER_CONNECT);
		if (scManager == IntPtr.Zero)
			ThrowLastWin32Error();
		try {
			var service = OpenService(scManager, name, SERVICE_CHANGE_CONFIG);
			if (service == IntPtr.Zero)
				ThrowLastWin32Error();
			try {
				var desc = new ServiceDescription { lpDescription = description };
				if (!ChangeServiceConfig2(service, SERVICE_CONFIG_DESCRIPTION, ref desc))
					ThrowLastWin32Error();
			}
			finally {
				CloseServiceHandle(service);
			}
		}
		finally {
			CloseServiceHandle(scManager);
		}
	}

	public static void Delete(string name) {
		var scManager = OpenSCManager(null, null, SC_MANAGER_CONNECT);
		if (scManager == IntPtr.Zero)
			ThrowLastWin32Error();
		try {
			var service = OpenService(scManager, name, DELETE);
			if (service == IntPtr.Zero)
				ThrowLastWin32Error();
			try {
				if (!NativeDeleteService(service))
					ThrowLastWin32Error();
			}
			finally {
				CloseServiceHandle(service);
			}
		}
		finally {
			CloseServiceHandle(scManager);
		}
	}

	#region Helpers
	private static void ThrowLastWin32Error() => throw new Win32Exception(Marshal.GetLastWin32Error());

	private static uint ToNativeStartType(ServiceStartType type) => type switch {
		ServiceStartType.Auto        => SERVICE_AUTO_START,
		ServiceStartType.DelayedAuto => SERVICE_AUTO_START,
		ServiceStartType.Disabled    => SERVICE_DISABLED,
		_                            => SERVICE_DEMAND_START
	};

	private static IntPtr MarshalDependencies(string[]? dependencies) {
		if (dependencies is null || dependencies.Length == 0)
			return IntPtr.Zero;

		// Build double-null-terminated multi-string: "dep1\0dep2\0\0"
		int totalChars = dependencies.Sum(d => d.Length + 1) + 1;
		var buffer = new char[totalChars];
		var pos = 0;
		foreach (string dep in dependencies) {
			dep.CopyTo(0, buffer, pos, dep.Length);
			pos += dep.Length + 1;
		}

		var ptr = Marshal.AllocHGlobal(totalChars * sizeof(char));
		Marshal.Copy(buffer, 0, ptr, totalChars);
		return ptr;
	}
	#endregion

	#region Constants
	private const uint SC_MANAGER_CONNECT = 0x0001;
	private const uint SC_MANAGER_CREATE_SERVICE = 0x0002;
	private const uint SERVICE_ALL_ACCESS = 0xF01FF;
	private const uint SERVICE_CHANGE_CONFIG = 0x0002;
	private const uint DELETE = 0x00010000;
	private const uint SERVICE_WIN32_OWN_PROCESS = 0x00000010;
	private const uint SERVICE_AUTO_START = 0x00000002;
	private const uint SERVICE_DEMAND_START = 0x00000003;
	private const uint SERVICE_DISABLED = 0x00000004;
	private const uint SERVICE_ERROR_NORMAL = 0x00000001;
	private const uint SERVICE_CONFIG_DESCRIPTION = 1;
	private const uint SERVICE_CONFIG_DELAYED_AUTO_START_INFO = 3;
	#endregion

	#region P/Invoke
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct ServiceDescription {
		[MarshalAs(UnmanagedType.LPWStr)]
		public string lpDescription;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct ServiceDelayedAutoStartInfo {
		[MarshalAs(UnmanagedType.Bool)]
		public bool fDelayedAutostart;
	}

	[DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	private static extern IntPtr OpenSCManager(
		string? machineName,
		string? databaseName,
		uint desiredAccess
	);

	[DllImport("advapi32.dll", EntryPoint = "CreateServiceW", SetLastError = true, CharSet = CharSet.Unicode)]
	private static extern IntPtr CreateService(
		IntPtr hSCManager,
		string serviceName,
		string? displayName,
		uint desiredAccess,
		uint serviceType,
		uint startType,
		uint errorControl,
		string binaryPathName,
		string? loadOrderGroup,
		IntPtr tagId,
		IntPtr dependencies,
		string? serviceStartName,
		string? password
	);

	[DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	private static extern IntPtr OpenService(
		IntPtr hSCManager,
		string serviceName,
		uint desiredAccess
	);

	[DllImport("advapi32.dll", EntryPoint = "DeleteService", SetLastError = true)]
	private static extern bool NativeDeleteService(IntPtr hService);

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern bool CloseServiceHandle(IntPtr hSCObject);

	[DllImport("advapi32.dll", EntryPoint = "ChangeServiceConfig2W", SetLastError = true, CharSet = CharSet.Unicode)]
	private static extern bool ChangeServiceConfig2(
		IntPtr hService,
		uint dwInfoLevel,
		ref ServiceDescription lpInfo
	);

	[DllImport("advapi32.dll", EntryPoint = "ChangeServiceConfig2W", SetLastError = true)]
	private static extern bool ChangeServiceConfig2(
		IntPtr hService,
		uint dwInfoLevel,
		ref ServiceDelayedAutoStartInfo lpInfo
	);
	#endregion
}