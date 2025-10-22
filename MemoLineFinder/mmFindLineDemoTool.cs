using MemoLibV2.ImageProcess.MemoDataType;
using MemoLibV2.ImageProcess.MemoDataType.CalculationDataType;
using MemoLibV2.ImageProcess.MemoDataType.SelectableObject;
using MemoLibV2.ImageProcess.MemoTool.MemoImageProcess;
using Newtonsoft.Json;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace MemoLibV2.ImageProcess.MemoTool.SerachTool
{
    public class FindLineOutputParam : ImageProcessOutputParam
    {
        public new mmSegment Results { get; internal set; }
        public override void Dispose()
        {
            base.Dispose();
        }
    }

    internal class CaliperInfo
    {
        public mmPoint Center { get; set; }
        public mmPoint Direction { get; set; }          // Search direction (vuông góc segment)
        public mmPoint Perpendicular { get; set; }      // Parallel to segment (song song segment)
        public double Length { get; set; }              // Search length (chiều dài tìm kiếm)
        public double Width { get; set; }               // Caliper width (chiều rộng caliper)
    }

    internal class EdgePoint
    {
        public mmPoint Position { get; set; }
        public double Strength { get; set; }
    }

    public enum eCaliperSearchDirection
    {
        LeftToRight,
        RightToLeft,
        TopToBottom,
        BottomToTop
    }

    public enum eCaliperPolarity
    {
        DarkToLight,  // Tối sang sáng
        LightToDark,  // Sáng sang tối
        Both          // Cả hai
    }

    public enum eCaliperTransition
    {
        First,        // Edge đầu tiên
        Last,         // Edge cuối cùng
        Strongest,    // Edge mạnh nhất
        All           // Tất cả edges
    }


    public class mmFindLineDemoTool : mmImageSearchTool
    {

        [Category("Output Params")]
        [Description("")]
        [JsonIgnore]
        [Xceed.Wpf.Toolkit.PropertyGrid.Attributes.ExpandableObject]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public new FindLineOutputParam OutputParam { get; internal set; } = new FindLineOutputParam();


        private mmSelectableCaliperLine LineSearchRegion { get => this.SearchRegion as mmSelectableCaliperLine; }


        public double Threshold { get => _Threshold; set { _Threshold = value; Notify(); } }
        public eCaliperTransition Transition { get => _Transition; set { _Transition = value; Notify(); } }
        public eCaliperSearchDirection SearchDirection { get => _SearchDirection; set { _SearchDirection = value; Notify(); } }
        public eCaliperPolarity Polarity { get => _Polarity; set { _Polarity = value; Notify(); } }

        public int NumIgnore { get => _NumIgnore; set { _NumIgnore = value; Notify(); } }

        int _NumIgnore = 0;
        double _Threshold = 30;
        eCaliperTransition _Transition = eCaliperTransition.Strongest;
        eCaliperSearchDirection _SearchDirection = eCaliperSearchDirection.LeftToRight;
        eCaliperPolarity _Polarity = eCaliperPolarity.Both;



        public mmFindLineDemoTool()
        {
            this.SearchRegion = new mmSelectableCaliperLine();
        }

        public override void Run()
        {
            DateTime st = DateTime.Now;

            if (this.InputParam == null)
            {
                RunState = ToolState.Error;
                RunMessage = "InputParam is null!";
                Ran?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (this.InputParam.InputImage == null)
            {
                RunState = ToolState.Error;
                RunMessage = "Input Image is null!";
                Ran?.Invoke(this, EventArgs.Empty);
                return;
            }

            this.OutputParam.Dispose();
            ClearImageView();

            if (IsGenerateOutputImage) AddImageView(nameof(InputParam.InputImage), new mmImage(InputParam.InputImage));

            try
            {
                Mat grayImage = new Mat();
                List<mmShape> graphics = new List<mmShape>();

                if (this.InputParam.InputImage.Channels() > 1)
                {
                    Cv2.CvtColor(this.InputParam.InputImage, grayImage, ColorConversionCodes.BGR2GRAY);
                }
                else
                {
                    this.InputParam.InputImage.CopyTo(grayImage);
                }

                List<mmPoint> DetectedPoints = new List<mmPoint>();

                List<CaliperInfo> calipers = GenerateCalipers();

                foreach (CaliperInfo caliper in calipers)
                {
                    var edgePoints = FindEdgesInCaliper(grayImage, caliper);

                    if (edgePoints != null && edgePoints.Count > 0)
                    {
                        // Lấy điểm theo transition mode
                        var selectedPoint = SelectPointByTransition(edgePoints);
                        if (selectedPoint != null)
                        {
                            DetectedPoints.Add(selectedPoint);
                        }
                    }
                }

                double FitError;

                // 4. Fit line từ detected points (Least Squares)
                mmSegment ResultLine = FitLineToPoints(DetectedPoints, out FitError);

                // MODIFIED: Apply NumIgnore logic - remove farthest NumIgnore points and refit
                if (_NumIgnore > 0 && DetectedPoints.Count > _NumIgnore + 2)
                {
                    RemoveFarthestPointsAndRefit(ref DetectedPoints, ref ResultLine, ref FitError, _NumIgnore);
                }

                if (IsGenerateOutputImage)
                {
                    graphics.AddRange(DetectedPoints);
                    graphics.Add(new mmLine(ResultLine));

                    grayImage.Dispose();

                    Mat outMat = new Mat();
                    this.InputParam.InputImage.CopyTo(outMat);

                    this.OutputParam.Results = ResultLine;
                    this.OutputParam.OutputImage = new mmImage(outMat, graphics);

                    this.AddImageView("OutputImage", this.OutputParam.OutputImage);
                }

                DateTime endTime = DateTime.Now;
                TactTime = endTime - st;
                RunState = ToolState.Done;
                RunMessage = "Done";
                if (this.IsSaveOutputImage)
                {
                    this.OutputParam.OutputImage.Save(PathSaveOutputImage, endTime.ToString("yyyy_MM_dd_HH_mm_ss_fff"), SaveImageFormat);
                }
            }
            catch (Exception ex)
            {
                RunMessage = ex.Message;
                RunState = ToolState.Error;
            }

            Ran?.Invoke(this, EventArgs.Empty);


        }

        private List<CaliperInfo> GenerateCalipers()
        {
            List<CaliperInfo> calipers = new List<CaliperInfo>();

            double dx = LineSearchRegion.CaliperSegment.EndPoint.X - LineSearchRegion.CaliperSegment.StartPoint.X;
            double dy = LineSearchRegion.CaliperSegment.EndPoint.Y - LineSearchRegion.CaliperSegment.StartPoint.Y;
            double segmentLength = Math.Sqrt(dx * dx + dy * dy);

            // Vector đơn vị SONG SONG với segment
            double parallelX = dx / segmentLength;
            double parallelY = dy / segmentLength;

            // Vector VUÔNG GÓC với segment (search direction - hướng tìm kiếm)
            double searchDirX = -parallelY;
            double searchDirY = parallelX;

            // Điều chỉnh search direction theo user setting
            if (this.SearchDirection == eCaliperSearchDirection.RightToLeft ||
                this.SearchDirection == eCaliperSearchDirection.BottomToTop)
            {
                searchDirX = -searchDirX;
                searchDirY = -searchDirY;
            }

            int numCalipers = LineSearchRegion.NumCalipers;
            double spacing = segmentLength / (numCalipers + 1);

            for (int i = 1; i <= numCalipers; i++)
            {
                double t = i * spacing;

                mmPoint center = new mmPoint(
                    LineSearchRegion.CaliperSegment.StartPoint.X + t * parallelX,
                    LineSearchRegion.CaliperSegment.StartPoint.Y + t * parallelY
                );

                CaliperInfo caliper = new CaliperInfo
                {
                    Center = center,
                    Direction = new mmPoint(searchDirX, searchDirY),           
                    Perpendicular = new mmPoint(parallelX, parallelY),         
                    Length = LineSearchRegion.CaliperLength,
                    Width = LineSearchRegion.CaliperWidth
                };

                calipers.Add(caliper);
            }

            return calipers;
        }


        // ==========================================
        // FindEdgesInCaliper - Dùng projection + threshold
        // ==========================================
        private List<EdgePoint> FindEdgesInCaliper(Mat grayImage, CaliperInfo caliper)
        {
            List<EdgePoint> edges = new List<EdgePoint>();

            // 1. Extract profile (projection dọc theo Direction)
            double[] profile = ExtractCaliperProfile(grayImage, caliper);

            if (profile == null || profile.Length < 2)
                return edges;

            // 2. Tính gradient (sobel-like: central difference)
            double[] gradient = new double[profile.Length];
            for (int i = 1; i < profile.Length - 1; i++)
            {
                gradient[i] = (profile[i + 1] - profile[i - 1]) / 2.0;
            }

            // 3. Find peaks trong gradient (tương tự threshold)
            for (int i = 1; i < gradient.Length - 1; i++)
            {
                double strength = Math.Abs(gradient[i]);
                bool isDarkToLight = gradient[i] > 0;
                bool isLightToDark = gradient[i] < 0;

                // Check polarity filter
                bool passPolarity = false;
                if (_Polarity == eCaliperPolarity.Both)
                    passPolarity = true;
                else if (_Polarity == eCaliperPolarity.DarkToLight && isDarkToLight)
                    passPolarity = true;
                else if (_Polarity == eCaliperPolarity.LightToDark && isLightToDark)
                    passPolarity = true;

                if (!passPolarity)
                    continue;

                // Check threshold
                if (strength < _Threshold)
                    continue;

                // Check local maximum
                if (Math.Abs(gradient[i]) > Math.Abs(gradient[i - 1]) &&
                    Math.Abs(gradient[i]) > Math.Abs(gradient[i + 1]))
                {
                    // Map index i về image coordinates
                    double offset = (i / (double)profile.Length - 0.5) * caliper.Length;

                    mmPoint edgePos = new mmPoint(
                        caliper.Center.X + offset * caliper.Direction.X,
                        caliper.Center.Y + offset * caliper.Direction.Y
                    );

                    edges.Add(new EdgePoint
                    {
                        Position = edgePos,
                        Strength = strength
                    });
                }
            }

            return edges;
        }


        // ==========================================
        // ExtractCaliperProfile - Average perpendicular to search direction
        // ==========================================
        private double[] ExtractCaliperProfile(Mat image, CaliperInfo caliper)
        {
            int length = (int)caliper.Length;
            int width = (int)caliper.Width;

            if (length <= 0 || width <= 0)
                return null;

            // Tạo transform matrix để crop vùng caliper
            Mat transformMatrix = GetCaliperTransformMatrix(caliper, length, width);

            // Warp image về caliper rectangle
            Mat croppedRegion = new Mat();
            Cv2.WarpAffine(image, croppedRegion, transformMatrix, new Size(length, width),
                           InterpolationFlags.Linear, BorderTypes.Constant, Scalar.All(0));

            // Tính profile bằng cách average theo chiều Width (Y-axis của cropped image)
            double[] profile = new double[length];

            for (int x = 0; x < length; x++)
            {
                double sum = 0;
                int count = 0;

                for (int y = 0; y < width; y++)
                {
                    byte val = croppedRegion.At<byte>(y, x);
                    sum += val;
                    count++;
                }

                profile[x] = count > 0 ? sum / count : 0;
            }

            croppedRegion.Dispose();
            return profile;
        }

        private Mat GetCaliperTransformMatrix(CaliperInfo caliper, int length, int width)
        {
            // Định nghĩa 3 điểm trong output coordinate system (cropped rectangle)
            // Cropped image layout:
            //   X-axis (0 → length-1): Profile direction (dọc theo Direction)
            //   Y-axis (0 → width-1): Averaging direction (dọc theo Perpendicular)

            Point2f[] srcPoints = new Point2f[3];
            srcPoints[0] = new Point2f(0, 0);              // Top-left
            srcPoints[1] = new Point2f(length - 1, 0);     // Top-right (move along profile direction)
            srcPoints[2] = new Point2f(0, width - 1);      // Bottom-left (move along averaging direction)

            // Map về image coordinate system
            Point2f[] dstPoints = new Point2f[3];

            // Tính bán kính theo từng hướng
            double halfLength = caliper.Length / 2.0;
            double halfWidth = caliper.Width / 2.0;

            // Top-left: Center - halfLength*Direction - halfWidth*Perpendicular
            dstPoints[0] = new Point2f(
                (float)(caliper.Center.X - halfLength * caliper.Direction.X - halfWidth * caliper.Perpendicular.X),
                (float)(caliper.Center.Y - halfLength * caliper.Direction.Y - halfWidth * caliper.Perpendicular.Y)
            );

            // Top-right: Center + halfLength*Direction - halfWidth*Perpendicular
            // (di chuyển theo Direction = profile direction)
            dstPoints[1] = new Point2f(
                (float)(caliper.Center.X + halfLength * caliper.Direction.X - halfWidth * caliper.Perpendicular.X),
                (float)(caliper.Center.Y + halfLength * caliper.Direction.Y - halfWidth * caliper.Perpendicular.Y)
            );

            // Bottom-left: Center - halfLength*Direction + halfWidth*Perpendicular
            // (di chuyển theo Perpendicular = averaging direction)
            dstPoints[2] = new Point2f(
                (float)(caliper.Center.X - halfLength * caliper.Direction.X + halfWidth * caliper.Perpendicular.X),
                (float)(caliper.Center.Y - halfLength * caliper.Direction.Y + halfWidth * caliper.Perpendicular.Y)
            );

            // Tính affine transform matrix (inverse: từ image space → rectangle space)
            Mat transform = Cv2.GetAffineTransform(dstPoints, srcPoints);

            return transform;
        }

        /// <summary>
        /// Chọn point theo transition mode
        /// </summary>
        private mmPoint SelectPointByTransition(List<EdgePoint> edges)
        {
            if (edges.Count == 0) return null;

            switch (_Transition)
            {
                case eCaliperTransition.First:
                    return edges[0].Position;

                case eCaliperTransition.Last:
                    return edges[edges.Count - 1].Position;

                case eCaliperTransition.Strongest:
                    return edges.OrderByDescending(e => e.Strength).First().Position;

                case eCaliperTransition.All:
                    // Trả về strongest cho đơn giản
                    return edges.OrderByDescending(e => e.Strength).First().Position;

                default:
                    return edges[0].Position;
            }
        }

        /// <summary>
        /// Fit line từ points - có thể dùng Cv2.FitLine
        /// </summary>
        /// <summary>
        /// Alternative: Fit line bằng Least Squares (như code gốc)
        /// </summary>
        private mmSegment FitLineToPoints(List<mmPoint> points, out double error)
        {
            int n = points.Count;
            double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;

            foreach (var p in points)
            {
                sumX += p.X;
                sumY += p.Y;
                sumXY += p.X * p.Y;
                sumX2 += p.X * p.X;
            }

            // Line: y = mx + b
            double denom = n * sumX2 - sumX * sumX;
            double m, b;

            if (Math.Abs(denom) < 0.0001)
            {
                // Vertical line, use x = my + b thay vào
                double sumY2 = points.Sum(p => p.Y * p.Y);
                denom = n * sumY2 - sumY * sumY;
                m = (n * sumXY - sumX * sumY) / denom;
                b = (sumX - m * sumY) / n;

                // Tạo segment từ min/max Y
                double minY = points.Min(p => p.Y);
                double maxY = points.Max(p => p.Y);
                error = CalculateFitError(points, m, b, true);
                return new mmSegment(
                    new mmPoint(m * minY + b, minY),
                    new mmPoint(m * maxY + b, maxY)
                );
            }

            m = (n * sumXY - sumX * sumY) / denom;
            b = (sumY - m * sumX) / n;

            // Tạo segment từ min/max X
            double minX = points.Min(p => p.X);
            double maxX = points.Max(p => p.X);

            error = CalculateFitError(points, m, b, false);

            return new mmSegment(
                new mmPoint(minX, m * minX + b),
                new mmPoint(maxX, m * maxX + b)
            );
        }

        /// <summary>
        /// Tính error của line fit
        /// </summary>
        private double CalculateFitError(List<mmPoint> points, double m, double b, bool isVertical)
        {
            double sumError = 0;

            foreach (var p in points)
            {
                double dist;
                if (isVertical)
                    dist = Math.Abs(p.X - (m * p.Y + b));
                else
                    dist = Math.Abs(p.Y - (m * p.X + b)) / Math.Sqrt(1 + m * m);

                sumError += dist * dist;
            }

            return Math.Sqrt(sumError / points.Count);
        }

        /// <summary>
        /// Loại bỏ NumIgnore điểm xa nhất với fitted line, rồi fit lại line
        /// </summary>
        private void RemoveFarthestPointsAndRefit(ref List<mmPoint> points, ref mmSegment line, ref double error, int numIgnore)
        {
            if (numIgnore <= 0 || points.Count <= numIgnore + 2)
                return;

            // Tính khoảng cách của mỗi điểm đến line
            List<(mmPoint point, double distance)> pointDistances = new List<(mmPoint, double)>();

            foreach (var p in points)
            {
                double dist = line.DistanceFromPoint(p);
                pointDistances.Add((p, dist));
            }

            // Sắp xếp theo khoảng cách từ xa đến gần
            pointDistances.Sort((a, b) => b.distance.CompareTo(a.distance));

            // Loại bỏ numIgnore điểm xa nhất
            List<mmPoint> remainingPoints = new List<mmPoint>();
            for (int i = numIgnore; i < pointDistances.Count; i++)
            {
                remainingPoints.Add(pointDistances[i].point);
            }

            // Fit lại line với các điểm còn lại
            if (remainingPoints.Count >= 2)
            {
                //points = remainingPoints;
                line = FitLineToPoints(remainingPoints, out error);
            }


            remainingPoints = pointDistances.Select(x => x.point).ToList(); 
            for (int i = 0; i < numIgnore; i++)
            {
                remainingPoints[i].Stroke = new SolidColorBrush(Colors.Red);
            }
        }



    }

}