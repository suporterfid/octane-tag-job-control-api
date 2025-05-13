// OctaneTagJobControlAPI/Services/Storage/CircularLogBuffer.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OctaneTagJobControlAPI.Services.Storage
{
    /// <summary>
    /// A circular buffer for log entries that automatically rotates old entries
    /// </summary>
    public class CircularLogBuffer : IDisposable
    {
        private readonly string _logFilePath;
        private readonly int _maxEntries;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private readonly List<string> _buffer;
        private int _position = 0;
        private bool _isFull = false;
        private readonly FileStream _fileStream;
        private readonly StreamWriter _writer;

        public CircularLogBuffer(string logFilePath, int maxEntries, ILogger logger)
        {
            _logFilePath = logFilePath;
            _maxEntries = maxEntries;
            _logger = logger;
            _buffer = new List<string>(maxEntries);

            // Create directory if it doesn't exist
            Directory.CreateDirectory(Path.GetDirectoryName(logFilePath));

            // Open file stream with read/write sharing
            _fileStream = new FileStream(
                logFilePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);

            _writer = new StreamWriter(_fileStream);

            // Load existing log entries
            LoadExistingEntries();
        }

        public async Task AppendAsync(string entry)
        {
            await _semaphore.WaitAsync();
            try
            {
                // Add to buffer
                if (_buffer.Count < _maxEntries)
                {
                    _buffer.Add(entry);
                }
                else
                {
                    // Buffer is full, overwrite oldest entry
                    _buffer[_position] = entry;
                    _position = (_position + 1) % _maxEntries;
                    _isFull = true;
                }

                // Write to file
                await _writer.WriteLineAsync(entry);
                await _writer.FlushAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error appending to log buffer");
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<List<string>> GetEntriesAsync(int count)
        {
            if (count <= 0)
                return new List<string>();

            await _semaphore.WaitAsync();
            try
            {
                // Determine how many entries to return
                int availableCount = _isFull ? _maxEntries : _buffer.Count;
                int actualCount = Math.Min(count, availableCount);

                if (actualCount == 0)
                    return new List<string>();

                var result = new List<string>(actualCount);

                // Calculate start index
                int startIndex;
                if (_isFull)
                {
                    // If buffer is full, start at the oldest entry
                    startIndex = _position;
                }
                else
                {
                    // If buffer is not full, start at the beginning minus requested count
                    startIndex = Math.Max(0, _buffer.Count - actualCount);
                }

                // Collect entries
                for (int i = 0; i < actualCount; i++)
                {
                    int index = (startIndex + i) % _buffer.Count;
                    result.Add(_buffer[index]);
                }

                return result;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private void LoadExistingEntries()
        {
            try
            {
                // Reset position
                _position = 0;
                _isFull = false;

                // Go to beginning of file
                _fileStream.Seek(0, SeekOrigin.Begin);

                // Read existing entries
                using (var reader = new StreamReader(_fileStream, leaveOpen: true))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (_buffer.Count < _maxEntries)
                        {
                            _buffer.Add(line);
                        }
                        else
                        {
                            // Buffer is full, overwrite oldest entry
                            _buffer[_position] = line;
                            _position = (_position + 1) % _maxEntries;
                            _isFull = true;
                        }
                    }
                }

                // Position file stream at the end for appending
                _fileStream.Seek(0, SeekOrigin.End);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading existing log entries");
            }
        }

        public void Dispose()
        {
            _writer?.Dispose();
            _fileStream?.Dispose();
            _semaphore?.Dispose();
        }
    }
}