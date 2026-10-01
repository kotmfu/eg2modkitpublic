; xinput1_4 proxy export stubs. Each jumps through g_real[i] (filled in DllMain from the real
; system xinput1_4.dll). A tail jmp preserves every register/arg, so it works for any signature,
; including the undocumented NONAME ordinals (100..109). Order matches dllmain.cpp and exports.def.
PUBLIC g_real
.data
g_real dq 14 dup(0)
.code
s_XInputGetState PROC
    jmp qword ptr [g_real + 0]
s_XInputGetState ENDP
s_XInputSetState PROC
    jmp qword ptr [g_real + 8]
s_XInputSetState ENDP
s_XInputGetCapabilities PROC
    jmp qword ptr [g_real + 16]
s_XInputGetCapabilities ENDP
s_XInputEnable PROC
    jmp qword ptr [g_real + 24]
s_XInputEnable ENDP
s_XInputGetBatteryInformation PROC
    jmp qword ptr [g_real + 32]
s_XInputGetBatteryInformation ENDP
s_XInputGetKeystroke PROC
    jmp qword ptr [g_real + 40]
s_XInputGetKeystroke ENDP
s_XInputGetAudioDeviceIds PROC
    jmp qword ptr [g_real + 48]
s_XInputGetAudioDeviceIds ENDP
s_o100 PROC
    jmp qword ptr [g_real + 56]
s_o100 ENDP
s_o101 PROC
    jmp qword ptr [g_real + 64]
s_o101 ENDP
s_o102 PROC
    jmp qword ptr [g_real + 72]
s_o102 ENDP
s_o103 PROC
    jmp qword ptr [g_real + 80]
s_o103 ENDP
s_o104 PROC
    jmp qword ptr [g_real + 88]
s_o104 ENDP
s_o108 PROC
    jmp qword ptr [g_real + 96]
s_o108 ENDP
s_o109 PROC
    jmp qword ptr [g_real + 104]
s_o109 ENDP
END
