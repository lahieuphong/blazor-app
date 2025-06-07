using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.IO.Buffer;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Png;
using System.Buffers.Binary;
using Microsoft.AspNetCore.Components;

namespace BlazorApp.Services
{
    public class DicomImageInfo
    {
        public string Base64Image { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public double WindowCenter { get; set; }
        public double WindowWidth { get; set; }

        public string StudyDate { get; set; } = string.Empty;
        public string StudyTime { get; set; } = string.Empty;
        public string SeriesDescription { get; set; } = string.Empty;
        public string Institution { get; set; } = string.Empty;
        public string PatientID { get; set; } = string.Empty;
        public string PatientName { get; set; } = string.Empty;
        public string Modality { get; set; } = string.Empty;
        public string StudyInstanceUID { get; set; } = string.Empty;
        public string TransferSyntax { get; set; } = string.Empty;

        public double TE { get; set; }
        public double TR { get; set; }
        public double SliceLocation { get; set; }
        public double SliceThickness { get; set; }
        public double PixelSpacingX { get; set; }
        public double PixelSpacingY { get; set; }
        public string Orientation { get; set; } = string.Empty;

        public string FormattedTransferSyntax =>
            $"{TransferSyntax} ({(TransferSyntax.Contains("Little Endian") || TransferSyntax.Contains("Implicit VR") ? "Không nén" : "Nén")})";

        public ushort[] PixelData { get; set; } = Array.Empty<ushort>();
        public bool ShowPixelInfo { get; set; } = false;
        public int MouseX { get; set; }
        public int MouseY { get; set; }
        public ushort? PixelValue { get; set; }

        public ElementReference ImageRef { get; set; }
        public ElementReference PixelInfoRef { get; set; }



        public double ZoomFactor { get; set; } = 1.0;
        public double OffsetX { get; set; } = 0;
        public double OffsetY { get; set; } = 0;

        public string FormattedStudyDate => FormatDate(StudyDate);
        public string FormattedStudyTime => FormatTime(StudyTime);

        private string FormatDate(string rawDate)
        {
            if (DateTime.TryParseExact(rawDate, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var dt))
                return dt.ToString("yyyy-MM-dd");
            return rawDate;
        }

        private string FormatTime(string rawTime)
        {
            if (DateTime.TryParseExact(rawTime.Split('.')[0], "HHmmss", null, System.Globalization.DateTimeStyles.None, out var tm))
                return tm.ToString("HH:mm:ss");
            return rawTime;
        }

    }

    public class DicomService
    {
        public string? LastErrorMessage { get; private set; }




        public DicomImageInfo? LoadDicomInfo(string filePath)
        {
            try
            {
                var dicomFile = DicomFile.Open(filePath);
                var dataset = dicomFile.Dataset;
                var image = new DicomImage(dataset);
                var pixelData = DicomPixelData.Create(dataset);

                string base64 = ConvertDicomToBase64Png(image);
                int width = image.Width;
                int height = image.Height;
                (double windowCenter, double windowWidth) = GetWindowLevel(dataset, pixelData);

                var info = new DicomImageInfo
                {
                    Base64Image = $"data:image/png;base64,{base64}",
                    Width = width,
                    Height = height,
                    WindowCenter = windowCenter,
                    WindowWidth = windowWidth,

                    StudyDate = dataset.GetSingleValueOrDefault(DicomTag.StudyDate, string.Empty),
                    StudyTime = dataset.GetSingleValueOrDefault(DicomTag.StudyTime, string.Empty),
                    SeriesDescription = dataset.GetSingleValueOrDefault(DicomTag.SeriesDescription, string.Empty),
                    Institution = dataset.GetSingleValueOrDefault(DicomTag.InstitutionName, string.Empty),
                    PatientID = dataset.GetSingleValueOrDefault(DicomTag.PatientID, string.Empty),
                    PatientName = dataset.GetSingleValueOrDefault(DicomTag.PatientName, string.Empty),
                    Modality = dataset.GetSingleValueOrDefault(DicomTag.Modality, string.Empty),
                    StudyInstanceUID = dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, string.Empty),
                    TransferSyntax = dicomFile.FileMetaInfo.TransferSyntax?.UID?.Name ?? string.Empty,

                    TE = dataset.GetSingleValueOrDefault(DicomTag.EchoTime, 0.0),
                    TR = dataset.GetSingleValueOrDefault(DicomTag.RepetitionTime, 0.0),
                    SliceLocation = dataset.GetSingleValueOrDefault(DicomTag.SliceLocation, 0.0),
                    SliceThickness = dataset.GetSingleValueOrDefault(DicomTag.SliceThickness, 0.0),
                    PixelSpacingX = GetPixelSpacing(dataset, 0),
                    PixelSpacingY = GetPixelSpacing(dataset, 1),
                    Orientation = GetOrientation(dataset),
                    PixelData = ExtractPixelData(pixelData, width, height)
                };
                return info;
            }
            catch (DicomImagingException ex) when (ex.Message.Contains("JPEG 2000"))
            {
                LastErrorMessage = "JPEG 2000 chưa được hỗ trợ.";
            }
            catch (Exception ex)
            {
                LastErrorMessage = $"Lỗi khi đọc DICOM: {ex.Message}";
            }

            return null;
        }

        private static string ConvertDicomToBase64Png(DicomImage dicomImage)
        {
            using var rendered = dicomImage.RenderImage();
            using var imageSharp = ConvertToImageSharp(rendered);
            using var ms = new MemoryStream();
            imageSharp.Save(ms, new PngEncoder());
            return Convert.ToBase64String(ms.ToArray());
        }

        private static Image<Rgba32> ConvertToImageSharp(IImage image)
        {
            var result = new Image<Rgba32>(image.Width, image.Height);
            result.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < image.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < image.Width; x++)
                    {
                        var pixel = image.GetPixel(x, y);
                        row[x] = new Rgba32(pixel.R, pixel.G, pixel.B, pixel.A);
                    }
                }
            });
            return result;
        }

        private static (double center, double width) GetWindowLevel(DicomDataset dataset, DicomPixelData pixelData)
        {
            double defaultCenter = pixelData.PixelRepresentation == PixelRepresentation.Unsigned ? 128 : 0;
            double defaultWidth = 256;

            double center = dataset.GetSingleValueOrDefault(DicomTag.WindowCenter, defaultCenter);
            double width = dataset.GetSingleValueOrDefault(DicomTag.WindowWidth, defaultWidth);
            return (center, width);
        }

        private static double GetPixelSpacing(DicomDataset dataset, int index)
        {
            if (dataset.TryGetValues(DicomTag.PixelSpacing, out double[] spacing) && spacing.Length > index)
                return spacing[index];
            return 0.0;
        }

        private static string GetOrientation(DicomDataset dataset)
        {
            if (dataset.TryGetValues(DicomTag.ImageOrientationPatient, out double[] values) && values.Length >= 6)
            {
                var angleLR = Math.Round(Math.Atan2(values[0], values[1]) * (180 / Math.PI), 2);
                var angleSI = Math.Round(Math.Atan2(values[4], values[5]) * (180 / Math.PI), 2);
                return $"L-R: {angleLR}°, S-I: {angleSI}°";
            }
            return string.Empty;
        }

        private static ushort[] ExtractPixelData(DicomPixelData pixelData, int width, int height)
        {
            int pixelCount = width * height;
            ushort[] pixels = new ushort[pixelCount];

            if (!pixelData.Dataset.Contains(DicomTag.PixelData))
                return pixels;

            var frame = pixelData.GetFrame(0);
            var buffer = frame.Data;

            if (pixelData.BytesAllocated == 2)
            {
                for (int i = 0; i < pixelCount; i++)
                    pixels[i] = BinaryPrimitives.ReadUInt16LittleEndian(new ReadOnlySpan<byte>(buffer, i * 2, 2));
            }
            else if (pixelData.BytesAllocated == 1)
            {
                for (int i = 0; i < pixelCount; i++)
                    pixels[i] = buffer[i];
            }

            return pixels;
        }
        
        
    }
}