using StatePocket.Hosting;

namespace StatePocket.Tests.Hosting;

public sealed class CallToolExecutionGateTests
{
    [Fact]
    public async Task ExecuteReadAsync_AllowsConcurrentReaders()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var gate = new CallToolExecutionGate();
        TaskCompletionSource firstEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource secondEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstTask = gate.ExecuteReadAsync(
                                 (firstEntered, releaseFirst),
                                 static async (state, token) =>
                                 {
                                     state.firstEntered.SetResult();
                                     await state.releaseFirst.Task.WaitAsync(token);
                                     return 0;
                                 },
                                 cancellationToken
                             )
                            .AsTask();
        await firstEntered.Task.WaitAsync(cancellationToken);
        var secondTask = gate.ExecuteReadAsync(
                                  secondEntered,
                                  static (entered, _) =>
                                  {
                                      entered.SetResult();
                                      return ValueTask.FromResult(0);
                                  },
                                  cancellationToken
                              )
                             .AsTask();
        await secondEntered.Task.WaitAsync(cancellationToken);
        Assert.True(secondTask.IsCompletedSuccessfully);
        releaseFirst.SetResult();
        await Task.WhenAll(firstTask, secondTask);
    }

    [Fact]
    public async Task ExecuteWriteAsync_BlocksReadersUntilWriterCompletes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var gate = new CallToolExecutionGate();
        TaskCompletionSource writerEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseWriter = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource readerEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var writerTask = gate.ExecuteWriteAsync(
                                  (writerEntered, releaseWriter),
                                  static async (state, token) =>
                                  {
                                      state.writerEntered.SetResult();
                                      await state.releaseWriter.Task.WaitAsync(token);
                                      return 0;
                                  },
                                  cancellationToken
                              )
                             .AsTask();
        await writerEntered.Task.WaitAsync(cancellationToken);
        var readerTask = gate.ExecuteReadAsync(
                                  readerEntered,
                                  static (entered, _) =>
                                  {
                                      entered.SetResult();
                                      return ValueTask.FromResult(0);
                                  },
                                  cancellationToken
                              )
                             .AsTask();
        Assert.False(readerTask.IsCompleted);
        Assert.False(readerEntered.Task.IsCompleted);
        releaseWriter.SetResult();
        await Task.WhenAll(writerTask, readerTask);
    }

    [Fact]
    public async Task ExecuteWriteAsync_BlocksOtherWritersUntilWriterCompletes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var gate = new CallToolExecutionGate();
        TaskCompletionSource firstWriterEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseFirstWriter = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource secondWriterEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstWriterTask = gate.ExecuteWriteAsync(
                                       (firstWriterEntered, releaseFirstWriter),
                                       static async (state, token) =>
                                       {
                                           state.firstWriterEntered.SetResult();
                                           await state.releaseFirstWriter.Task.WaitAsync(token);
                                           return 0;
                                       },
                                       cancellationToken
                                   )
                                  .AsTask();
        await firstWriterEntered.Task.WaitAsync(cancellationToken);
        var secondWriterTask = gate.ExecuteWriteAsync(
                                        secondWriterEntered,
                                        static (entered, _) =>
                                        {
                                            entered.SetResult();
                                            return ValueTask.FromResult(0);
                                        },
                                        cancellationToken
                                    )
                                   .AsTask();
        Assert.False(secondWriterTask.IsCompleted);
        Assert.False(secondWriterEntered.Task.IsCompleted);
        releaseFirstWriter.SetResult();
        await Task.WhenAll(firstWriterTask, secondWriterTask);
    }

    [Fact]
    public async Task ExecuteReadAsync_DoesNotInvokeActionWhenCanceledBeforeEntering()
    {
        await using var gate = new CallToolExecutionGate();
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gate.ExecuteReadAsync(
                                                                               entered,
                                                                               static (actionEntered, _) =>
                                                                               {
                                                                                   actionEntered.SetResult();
                                                                                   return ValueTask.FromResult(0);
                                                                               },
                                                                               cancellationTokenSource.Token
                                                                           )
                                                                          .AsTask()
        );
        Assert.False(entered.Task.IsCompleted);
    }

    [Fact]
    public async Task ExecuteWriteAsync_DoesNotInvokeActionWhenCanceledWhileQueued()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var gate = new CallToolExecutionGate();
        TaskCompletionSource firstReaderEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseFirstReader = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource actionEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstReaderTask = gate.ExecuteReadAsync(
                                       (firstReaderEntered, releaseFirstReader),
                                       static async (state, token) =>
                                       {
                                           state.firstReaderEntered.SetResult();
                                           await state.releaseFirstReader.Task.WaitAsync(token);
                                           return 0;
                                       },
                                       cancellationToken
                                   )
                                  .AsTask();
        await firstReaderEntered.Task.WaitAsync(cancellationToken);
        using var cancellationTokenSource = new CancellationTokenSource();
        var canceledWriterTask = gate.ExecuteWriteAsync(
                                          actionEntered,
                                          static (entered, _) =>
                                          {
                                              entered.SetResult();
                                              return ValueTask.FromResult(0);
                                          },
                                          cancellationTokenSource.Token
                                      )
                                     .AsTask();
        await cancellationTokenSource.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledWriterTask);
        Assert.False(actionEntered.Task.IsCompleted);
        releaseFirstReader.SetResult();
        await firstReaderTask;
    }

    [Fact]
    public async Task ExecuteReadAsync_ReleasesLockWhenActionThrows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var gate = new CallToolExecutionGate();
        await Assert.ThrowsAsync<InvalidOperationException>(() => gate
                                                                 .ExecuteReadAsync(
                                                                      0,
                                                                      static (_, _) =>
                                                                          ValueTask.FromException<int>(
                                                                              new InvalidOperationException("boom")
                                                                          ),
                                                                      cancellationToken
                                                                  )
                                                                 .AsTask()
        );
        var result = await gate.ExecuteWriteAsync(
                                    42,
                                    static (value, _) => ValueTask.FromResult(value),
                                    cancellationToken
                                )
                               .AsTask();
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task ExecuteWriteAsync_ReleasesLockWhenActionThrows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var gate = new CallToolExecutionGate();
        await Assert.ThrowsAsync<InvalidOperationException>(() => gate
                                                                 .ExecuteWriteAsync(
                                                                      0,
                                                                      static (_, _) =>
                                                                          ValueTask.FromException<int>(
                                                                              new InvalidOperationException("boom")
                                                                          ),
                                                                      cancellationToken
                                                                  )
                                                                 .AsTask()
        );
        var result = await gate.ExecuteReadAsync(
                                    42,
                                    static (value, _) => ValueTask.FromResult(value),
                                    cancellationToken
                                )
                               .AsTask();
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task ExecuteReadAsync_PropagatesActionResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var gate = new CallToolExecutionGate();
        var result = await gate.ExecuteReadAsync(
                                    "hello",
                                    static (value, _) => ValueTask.FromResult(value.ToUpperInvariant()),
                                    cancellationToken
                                )
                               .AsTask();
        Assert.Equal("HELLO", result);
    }
}
