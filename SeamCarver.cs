using System;
using ILGPU;
using ILGPU.Runtime;

public class SeamCarver : IDisposable
{
    private Accelerator _gpu;

    private Action<Index2D, ArrayView2D<int, Stride2D.DenseY>, ArrayView2D<float, Stride2D.DenseY>> _calcEnergyKernel;
    private Action<Index2D, ArrayView2D<int, Stride2D.DenseY>, ArrayView1D<int, Stride1D.Dense>, ArrayView2D<int, Stride2D.DenseY>> _removeVerticalSeamKernel;
    private Action<Index2D, ArrayView2D<int, Stride2D.DenseY>, ArrayView1D<int, Stride1D.Dense>, ArrayView2D<int, Stride2D.DenseY>> _removeHorizontalSeamKernel;

    public SeamCarver(Accelerator gpu)
    {
        _gpu = gpu;
        _calcEnergyKernel = _gpu.LoadAutoGroupedStreamKernel<Index2D, ArrayView2D<int, Stride2D.DenseY>, ArrayView2D<float, Stride2D.DenseY>>(BackwardEnergyKernel);
        _removeVerticalSeamKernel = _gpu.LoadAutoGroupedStreamKernel<Index2D, ArrayView2D<int, Stride2D.DenseY>, ArrayView1D<int, Stride1D.Dense>, ArrayView2D<int, Stride2D.DenseY>>(RemoveVerticalSeamKernel);
        _removeHorizontalSeamKernel = _gpu.LoadAutoGroupedStreamKernel<Index2D, ArrayView2D<int, Stride2D.DenseY>, ArrayView1D<int, Stride1D.Dense>, ArrayView2D<int, Stride2D.DenseY>>(RemoveHorizontalSeamKernel);
    }

    public void CalculateEnergy(ArrayView2D<int, Stride2D.DenseY> colorImage, ArrayView2D<float, Stride2D.DenseY> energyMap)
    {
        _calcEnergyKernel(colorImage.Extent.ToIntIndex(), colorImage, energyMap);
    }

    // Helper method inside the kernel to convert packed BGRA int to grayscale intensity
    static float GetGray(int packedBgra)
    {
        int b = packedBgra & 0xFF;
        int g = (packedBgra >> 8) & 0xFF;
        int r = (packedBgra >> 16) & 0xFF;
        return 0.114f * b + 0.587f * g + 0.299f * r;
    }

    static void BackwardEnergyKernel(Index2D index, ArrayView2D<int, Stride2D.DenseY> colorImage, ArrayView2D<float, Stride2D.DenseY> energyMap)
    {
        int x = index.X;
        int y = index.Y;

        if (x == 0 || x >= colorImage.Extent.X - 1 || y == 0 || y >= colorImage.Extent.Y - 1)
        {
            energyMap[index] = 100000.0f;
            return;
        }

        float xGrad = GetGray(colorImage[new Index2D(x + 1, y)]) - GetGray(colorImage[new Index2D(x - 1, y)]);
        float yGrad = GetGray(colorImage[new Index2D(x, y + 1)]) - GetGray(colorImage[new Index2D(x, y - 1)]);

        energyMap[index] = MathF.Sqrt((xGrad * xGrad) + (yGrad * yGrad));
    }

    // --- VERTICAL SEAM LOGIC (Width Reduction) ---
    public int[] FindVerticalSeam(float[,] energyMap, int width, int height)
    {
        float[,] cumulativeEnergy = new float[width, height];
        int[,] backtrack = new int[width, height];

        for (int x = 0; x < width; x++) cumulativeEnergy[x, 0] = energyMap[x, 0];

        for (int y = 1; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float minPrev = cumulativeEnergy[x, y - 1];
                int minX = x;

                if (x > 0 && cumulativeEnergy[x - 1, y - 1] < minPrev) { minPrev = cumulativeEnergy[x - 1, y - 1]; minX = x - 1; }
                if (x < width - 1 && cumulativeEnergy[x + 1, y - 1] < minPrev) { minPrev = cumulativeEnergy[x + 1, y - 1]; minX = x + 1; }

                cumulativeEnergy[x, y] = energyMap[x, y] + minPrev;
                backtrack[x, y] = minX;
            }
        }

        int[] seam = new int[height];
        float minBottom = float.MaxValue;
        int currX = 0;

        for (int x = 0; x < width; x++)
        {
            if (cumulativeEnergy[x, height - 1] < minBottom) { minBottom = cumulativeEnergy[x, height - 1]; currX = x; }
        }

        for (int y = height - 1; y >= 0; y--)
        {
            seam[y] = currX;
            currX = backtrack[currX, y];
        }
        return seam;
    }

    public void RemoveVerticalSeam(ArrayView2D<int, Stride2D.DenseY> oldImage, ArrayView1D<int, Stride1D.Dense> seamPath, ArrayView2D<int, Stride2D.DenseY> newImage)
    {
        _removeVerticalSeamKernel(newImage.Extent.ToIntIndex(), oldImage, seamPath, newImage);
    }

    static void RemoveVerticalSeamKernel(Index2D index, ArrayView2D<int, Stride2D.DenseY> oldImage, ArrayView1D<int, Stride1D.Dense> seamPath, ArrayView2D<int, Stride2D.DenseY> newImage)
    {
        int x = index.X, y = index.Y, seamX = seamPath[y];
        newImage[index] = (x < seamX) ? oldImage[new Index2D(x, y)] : oldImage[new Index2D(x + 1, y)];
    }

    // --- HORIZONTAL SEAM LOGIC (Height Reduction) ---
    public int[] FindHorizontalSeam(float[,] energyMap, int width, int height)
    {
        float[,] cumulativeEnergy = new float[width, height];
        int[,] backtrack = new int[width, height];

        for (int y = 0; y < height; y++) cumulativeEnergy[0, y] = energyMap[0, y];

        for (int x = 1; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                float minPrev = cumulativeEnergy[x - 1, y];
                int minY = y;

                if (y > 0 && cumulativeEnergy[x - 1, y - 1] < minPrev) { minPrev = cumulativeEnergy[x - 1, y - 1]; minY = y - 1; }
                if (y < height - 1 && cumulativeEnergy[x - 1, y + 1] < minPrev) { minPrev = cumulativeEnergy[x - 1, y + 1]; minY = y + 1; }

                cumulativeEnergy[x, y] = energyMap[x, y] + minPrev;
                backtrack[x, y] = minY;
            }
        }

        int[] seam = new int[width];
        float minRight = float.MaxValue;
        int currY = 0;

        for (int y = 0; y < height; y++)
        {
            if (cumulativeEnergy[width - 1, y] < minRight) { minRight = cumulativeEnergy[width - 1, y]; currY = y; }
        }

        for (int x = width - 1; x >= 0; x--)
        {
            seam[x] = currY;
            currY = backtrack[x, currY];
        }
        return seam;
    }

    public void RemoveHorizontalSeam(ArrayView2D<int, Stride2D.DenseY> oldImage, ArrayView1D<int, Stride1D.Dense> seamPath, ArrayView2D<int, Stride2D.DenseY> newImage)
    {
        _removeHorizontalSeamKernel(newImage.Extent.ToIntIndex(), oldImage, seamPath, newImage);
    }

    static void RemoveHorizontalSeamKernel(Index2D index, ArrayView2D<int, Stride2D.DenseY> oldImage, ArrayView1D<int, Stride1D.Dense> seamPath, ArrayView2D<int, Stride2D.DenseY> newImage)
    {
        int x = index.X, y = index.Y, seamY = seamPath[x];
        newImage[index] = (y < seamY) ? oldImage[new Index2D(x, y)] : oldImage[new Index2D(x, y + 1)];
    }

    public void Dispose() { }
}