# Line Caliper Detection Algorithm

## Overview

The `mmFindLineTool` is a robust line detection algorithm implemented in C# using the OpenCvSharp library for advanced image processing. It is designed to detect straight lines in grayscale or color images by employing a caliper-based edge detection approach. This tool is part of the `MemoLibV2` library and is well-suited for industrial vision applications, such as quality control, object alignment, and edge-based measurements.

## Features

- **Caliper-Based Edge Detection**: Utilizes multiple calipers along a user-defined line segment to detect edge points with high precision.
<img width="1920" height="1031" alt="image" src="https://github.com/user-attachments/assets/9990dc5e-2afa-4b29-be3f-86cdd7e2cc06" />
<img width="1915" height="991" alt="image" src="https://github.com/user-attachments/assets/1ecb0050-0d0c-4899-a96f-8efb7ca48e19" />

- **Configurable Search Parameters**:
  - **Search Direction**: Supports `LeftToRight`, `RightToLeft`, `TopToBottom`, and `BottomToTop`.
  - **Polarity**: Detects transitions (`DarkToLight`, `LightToDark`, or `Both`).
  - **Transition Mode**: Selects edge points (`First`, `Last`, `Strongest`, or `All`).
- **Thresholding**: Filters edge points based on a user-defined gradient strength threshold.
- **Outlier Removal**: Ignores a specified number of outlier points (`NumIgnore`) to enhance line fitting accuracy.
- **Line Fitting**: Employs the Least Squares method to fit a line to detected edge points, with error calculation.
- **Visualization**: Generates output images with detected points and fitted lines for inspection.
- **Performance Tracking**: Measures tact time for runtime analysis.

## How It Works

1. **Input Processing**:
   - Accepts a color or grayscale image.
   - Converts color images to grayscale for processing if needed.
2. **Caliper Generation**:
   - Generates calipers along a user-defined line segment (`mmSelectableCaliperLine`).
   - Each caliper defines a center, search direction (perpendicular to the segment), and averaging direction (parallel to the segment).
3. **Edge Detection**:
   - Extracts intensity profiles along the search direction of each caliper.
   - Computes gradients to identify edge points based on polarity and threshold.
   - Selects edge points according to the specified transition mode.
4. **Line Fitting**:
   - Fits a line to detected edge points using the Least Squares method.
   - Optionally removes outlier points (`NumIgnore`) and refits the line for improved accuracy.
5. **Output**:
   - Returns a `FindLineOutputParam` containing the fitted line (`mmSegment`) and an optional output image with visualized results.
   - Supports saving the output image with a timestamp for logging or debugging.

## Key Components

- **`mmFindLineTool`**: Main class implementing the line detection algorithm, inheriting from `mmImageSearchTool`.
- **`FindLineOutputParam`**: Stores the output, including the fitted line segment (`mmSegment`).
- **`CaliperInfo`**: Defines caliper properties (center, direction, perpendicular vector, length, width).
- **`EdgePoint`**: Represents detected edge points with position and gradient strength.
- **Enums**:
  - `eCaliperSearchDirection`: Defines search directions.
  - `eCaliperPolarity`: Specifies edge transition types.
  - `eCaliperTransition`: Determines edge point selection mode.

## Installation

### Prerequisites

- .NET Framework or .NET Core
- [OpenCvSharp](https://github.com/shimat/opencvsharp) for image processing
- [Newtonsoft.Json](https://www.newtonsoft.com/json) for serialization
- `MemoLibV2` dependencies for custom data types (`mmPoint`, `mmSegment`, etc.)

### Setup

1. Clone the repository:
   ```bash
   git clone https://github.com/your-repo/mmFindLineTool.git
   ```
2. Install dependencies via NuGet:
   ```bash
   dotnet add package OpenCvSharp4
   dotnet add package Newtonsoft.Json
   ```
3. Include `MemoLibV2` as a project dependency or library reference.

## Usage

### Example Code

```csharp
using MemoLibV2.ImageProcess.MemoTool.SearchTool;

var findLineTool = new mmFindLineTool
{
    Threshold = 30,
    SearchDirection = eCaliperSearchDirection.LeftToRight,
    Polarity = eCaliperPolarity.Both,
    Transition = eCaliperTransition.Strongest,
    NumIgnore = 2
};

// Set input image and search region
findLineTool.InputParam = new ImageProcessInputParam
{
    InputImage = new Mat("input_image.jpg", ImreadModes.Color)
};
findLineTool.SearchRegion = new mmSelectableCaliperLine
{
    CaliperSegment = new mmSegment(new mmPoint(100, 100), new mmPoint(200, 200)),
    NumCalipers = 10,
    CaliperLength = 50,
    CaliperWidth = 20
};

// Run the tool
findLineTool.Run();

// Access results
var resultLine = findLineTool.OutputParam.Results;
Console.WriteLine($"Detected Line: Start = ({resultLine.StartPoint.X}, {resultLine.StartPoint.Y}), End = ({resultLine.EndPoint.X}, {resultLine.EndPoint.Y})");
```

### Configuration

- **Threshold**: Minimum gradient strength for edge detection (default: `30`).
- **NumIgnore**: Number of outlier points to ignore during line fitting (default: `0`).
- **SearchRegion**: Defines the line segment and caliper properties (e.g., number of calipers, length, width).
- **IsGenerateOutputImage**: Set to `true` to generate a visual output with detected points and fitted line.
- **IsSaveOutputImage**: Set to `true` to save the output image with a timestamp.

## Applications

- Industrial vision systems for detecting edges or lines in manufactured parts.
- Automated quality control and inspection.
- Alignment and positioning in robotics or automation.
- Measurement of linear features in images.

## Limitations

- Requires a well-defined search region (`mmSelectableCaliperLine`) for accurate detection.
- Performance depends on image quality and parameter tuning (e.g., threshold, caliper size).
- Assumes detectable edges within the search region.

## Future Improvements

- Implement sub-pixel accuracy for edge detection.
- Optimize caliper profile extraction for faster processing.
- Enhance visualization options for better debugging.

## Contributing

Contributions are welcome! Please submit a pull request or open an issue to discuss improvements or bug fixes.

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.
