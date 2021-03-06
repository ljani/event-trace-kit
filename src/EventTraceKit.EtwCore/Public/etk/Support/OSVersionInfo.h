#pragma once
#include "CompilerSupport.h"
#include "Rtl.h"

#include <tuple>

class OSVersionInfo
{
public:
    OSVersionInfo()
        : versionInfo()
    {
        versionInfo.dwOSVersionInfoSize = sizeof(versionInfo);
        if (RtlGetVersion(&versionInfo) != 0)
            versionInfo = {};
    }

    bool IsWindowsXPOrGreater() const
    {
        return IsWindowsVersionOrGreater(HIBYTE(_WIN32_WINNT_WINXP),
                                         LOBYTE(_WIN32_WINNT_WINXP), 0);
    }

    bool IsWindowsXPSP1OrGreater() const
    {
        return IsWindowsVersionOrGreater(HIBYTE(_WIN32_WINNT_WINXP),
                                         LOBYTE(_WIN32_WINNT_WINXP), 1);
    }

    bool IsWindowsXPSP2OrGreater() const
    {
        return IsWindowsVersionOrGreater(HIBYTE(_WIN32_WINNT_WINXP),
                                         LOBYTE(_WIN32_WINNT_WINXP), 2);
    }

    bool IsWindowsXPSP3OrGreater() const
    {
        return IsWindowsVersionOrGreater(HIBYTE(_WIN32_WINNT_WINXP),
                                         LOBYTE(_WIN32_WINNT_WINXP), 3);
    }

    bool IsWindowsVistaOrGreater() const
    {
        return IsWindowsVersionOrGreater(HIBYTE(_WIN32_WINNT_VISTA),
                                         LOBYTE(_WIN32_WINNT_VISTA), 0);
    }

    bool IsWindowsVistaSP1OrGreater() const
    {
        return IsWindowsVersionOrGreater(HIBYTE(_WIN32_WINNT_VISTA),
                                         LOBYTE(_WIN32_WINNT_VISTA), 1);
    }

    bool IsWindowsVistaSP2OrGreater() const
    {
        return IsWindowsVersionOrGreater(HIBYTE(_WIN32_WINNT_VISTA),
                                         LOBYTE(_WIN32_WINNT_VISTA), 2);
    }

    bool IsWindows7OrGreater() const
    {
        return IsWindowsVersionOrGreater(HIBYTE(_WIN32_WINNT_WIN7),
                                         LOBYTE(_WIN32_WINNT_WIN7), 0);
    }

    bool IsWindows7SP1OrGreater() const
    {
        return IsWindowsVersionOrGreater(HIBYTE(_WIN32_WINNT_WIN7),
                                         LOBYTE(_WIN32_WINNT_WIN7), 1);
    }

    bool IsWindows8OrGreater() const
    {
        return IsWindowsVersionOrGreater(HIBYTE(_WIN32_WINNT_WIN8),
                                         LOBYTE(_WIN32_WINNT_WIN8), 0);
    }

    bool IsWindows8Point1OrGreater() const
    {
        return IsWindowsVersionOrGreater(HIBYTE(_WIN32_WINNT_WINBLUE),
                                         LOBYTE(_WIN32_WINNT_WINBLUE), 0);
    }

    bool IsWindows10OrGreater() const
    {
        return IsWindowsVersionOrGreater(HIBYTE(_WIN32_WINNT_WIN10),
                                         LOBYTE(_WIN32_WINNT_WIN10), 0);
    }

    bool IsWindows10Version1507OrGreater() const
    {
        return IsWindowsVersionOrGreater(HIBYTE(_WIN32_WINNT_WIN10),
                                         LOBYTE(_WIN32_WINNT_WIN10), 10240);
    }

    bool IsWindows10Version1703OrGreater() const
    {
        return IsWindowsVersionOrGreater(HIBYTE(_WIN32_WINNT_WIN10),
                                         LOBYTE(_WIN32_WINNT_WIN10), 15063);
    }

    bool IsWindows10Version1607OrGreater() const
    {
        return IsWindowsVersionOrGreater(HIBYTE(_WIN32_WINNT_WIN10),
                                         LOBYTE(_WIN32_WINNT_WIN10), 14393);
    }

    bool IsWindows10Version1511OrGreater() const
    {
        return IsWindowsVersionOrGreater(HIBYTE(_WIN32_WINNT_WIN10),
                                         LOBYTE(_WIN32_WINNT_WIN10), 10586);
    }

    bool IsWindows10Version1709OrGreater() const
    {
        return IsWindowsVersionOrGreater(HIBYTE(_WIN32_WINNT_WIN10),
                                         LOBYTE(_WIN32_WINNT_WIN10), 16299);
    }

private:
    ETK_ALWAYS_INLINE
    bool IsWindowsVersionOrGreater(WORD majorVersion, WORD minorVersion,
                                   WORD servicePackMajor) const
    {
        return std::make_tuple(versionInfo.dwMajorVersion, versionInfo.dwMinorVersion,
                               versionInfo.wServicePackMajor) >=
               std::make_tuple(majorVersion, minorVersion, servicePackMajor);
    }

    OSVERSIONINFOEXW versionInfo;
};

inline OSVersionInfo const OSVersion;
