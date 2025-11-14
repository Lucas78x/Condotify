namespace CondotifyAPI.Services.Extensions
{
    public static class DeviceExtensions
    {
        public static bool IsInIntelbras(this DeviceTypeEnum deviceType)
        {
            return deviceType switch
            {
                DeviceTypeEnum.SS5520 or
                DeviceTypeEnum.SS5530MFFace or
                DeviceTypeEnum.SS5530MFFaceLite or
                DeviceTypeEnum.SS3530MFFaceW or
                DeviceTypeEnum.SS3430Bio or
                DeviceTypeEnum.SS3430MFBio or
                DeviceTypeEnum.SS7520FaceT or
                DeviceTypeEnum.SS7530Face or
                DeviceTypeEnum.SS3530MFFace or
                DeviceTypeEnum.SS3540MFFaceEx or
                DeviceTypeEnum.SS1540MFW or
                DeviceTypeEnum.SS1530MFW or
                DeviceTypeEnum.SS3540MFFaceBioEx or
                DeviceTypeEnum.SS3540MFFaceBio or
                DeviceTypeEnum.SS3532MFW or
                DeviceTypeEnum.SS3532MF or
                DeviceTypeEnum.SS3542MFW or
                DeviceTypeEnum.SS3531MF or
                DeviceTypeEnum.SS5531MFW or
                DeviceTypeEnum.SS5541MFW or
                DeviceTypeEnum.SS5532MFW or
                DeviceTypeEnum.SS5542MFW or
                DeviceTypeEnum.SS5430MFBioFT or
                DeviceTypeEnum.SS3541MF or
                DeviceTypeEnum.CT30002PB or
                DeviceTypeEnum.CT30004PB or
                DeviceTypeEnum.SS3710UHF
                    => true,
                _ => false
            };
        }

        public static bool IsInControlId(this DeviceTypeEnum deviceType)
        {
            return deviceType switch
            {
                DeviceTypeEnum.IdFace or
                DeviceTypeEnum.IdFaceMax
                    => true,
                _ => false
            };
        }
    }
}
