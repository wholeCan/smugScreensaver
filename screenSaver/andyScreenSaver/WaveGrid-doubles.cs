using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace WaveSim
{
    class WaveGrid
    {
        // Constants
        const int MinDimension = 5;     // We lose 2 rows, 2 cols along edges, so inner/dynamic area is at least 3x3
        const double Damping = 0.9375;
        const double SmoothingFactor = 2.0;     // Gives more weight to smoothing than to velocity

        // Private member data
        //private double[,] _buffer1;      // Buffers that store actual data
        //private double[,] _buffer2;

        private Point3DCollection _ptBuffer1;
        private Point3DCollection _ptBuffer2;
        private Int32Collection _triangleIndices;

        private uint _dimension;

        // Pointers to which buffers contain:
        //    - Current: Most recent data
        //    - Old: Earlier data
        // These two pointers will swap, pointing to buffer1/buffer2 as we cycle the buffers
        //private double[,] _currBuffer;    
        //private double[,] _oldBuffer;  

        private Point3DCollection _currBuffer;
        private Point3DCollection _oldBuffer;
                                           
        /// <summary>
        /// Construct new grid of a given dimension
        /// </summary>
        /// <param name="Dimension"></param>
        public WaveGrid(uint Dimension)
        {
            if (Dimension < MinDimension)
                throw new ApplicationException(string.Format("Dimension must be at least {0}", MinDimension.ToString()));

            _ptBuffer1 = new Point3DCollection(Dimension * Dimension);
            _ptBuffer2 = new Point3DCollection(Dimension * Dimension);
            _triangleIndices = new Int32Collection((Dimension - 1) * (Dimension - 1) * 2);

            //_buffer1 = new double[Dimension, Dimension];
            //_buffer2 = new double[Dimension, Dimension];

            _dimension = Dimension;

            //_currBuffer = _buffer2;
            //_oldBuffer = _buffer1;

            _currBuffer = _ptBuffer2;
            _oldBuffer = _ptBuffer1;
        }

        /// <summary>
        /// Access to underlying grid data
        /// </summary>
        //public double[,] Data
        //{
        //    get { return _currBuffer; }
        //}

        public Point3DCollection Points
        {
            get { return _currBuffer; }
        }

        /// <summary>
        /// Dimension of grid--same dimension for both X & Y
        /// </summary>
        public uint Dimension
        {
            get { return _dimension; }
        }

        /// <summary>
        /// Rezero entire grid
        /// </summary>
        public void Zero()
        {
            foreach (Point3D pt in _ptBuffer1)
                pt.Z = 0.0;

            foreach (Point3D pt in _ptBuffer2)
                pt.Z = 0.0;

            //for (int i = 0; i < _dimension; i++)
            //    for (int j = 0; j < _dimension; j++)
            //    {
            //        _buffer1[i, j] = 0.0;
            //        _buffer2[i, j] = 0.0;
            //    }
        }

        /// <summary>
        /// Set center of grid to some peak value (high point).  Leave
        /// rest of grid alone.  Note: If dimension is even, we're not 
        /// exactly at the center of the grid--no biggie.
        /// </summary>
        /// <param name="PeakValue"></param>
        public void SetCenterPeak(double PeakValue)
        {
            int nCenter = (int)_dimension / 2;

            // Change data in oldest buffer, then make newest buffer
            // become oldest by swapping
            _oldBuffer[nCenter, nCenter] = PeakValue;
            SwapBuffers();
        }

        /// <summary>
        /// Leave buffers in place, but change notation of which one is most recent
        /// </summary>
        private void SwapBuffers()
        {
            double[,] temp = _currBuffer;
            _currBuffer = _oldBuffer;
            _oldBuffer = temp;
        }

        /// <summary>
        /// Determine next state of entire grid, based on previous two states.
        /// This will have the effect of propagating ripples outward.
        /// </summary>
        public void ProcessWater()
        {
            // Note that we write into old buffer, which will then become our
            //    "current" buffer, and current will become old.  
            // I.e. What starts out in _currBuffer shifts into _oldBuffer and we 
            // write new data into _currBuffer.  But because we just swap pointers, 
            // we don't have to actually move data around.

            // When calculating data, we don't generate data for the cells around
            // the edge of the grid, because data smoothing looks at all adjacent
            // cells.  So instead of running [0,n-1], we run [1,n-2].

            double velocity;    // Rate of change from old to current
            double smoothed;    // Smoothed by adjacent cells
            double newHeight;

            for (int row = 1; row <= (_dimension - 2); row++)
            {
                for (int col = 1; col <= (_dimension - 2); col++)
                {
                    velocity = -1.0 * _oldBuffer[row, col];
                    smoothed = (_currBuffer[row - 1, col] +
                                _currBuffer[row + 1, col] +
                                _currBuffer[row, col - 1] +
                                _currBuffer[row, col + 1]) / 4.0;

                    // New height is combination of smoothing and velocity
                    newHeight = smoothed * SmoothingFactor + velocity;

                    // Damping
                    newHeight = newHeight * Damping;

                    // We write new data to old buffer
                    _oldBuffer[row, col] = newHeight;
                }
            }

            SwapBuffers();
        }
    }
}
