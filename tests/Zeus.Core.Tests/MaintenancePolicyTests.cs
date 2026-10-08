using Zeus.Core;

namespace Zeus.Core.Tests;

public sealed class MaintenancePolicyTests
{
    [Theory]
    [InlineData(MaintenanceActionId.RepairWindowsImage)]
    [InlineData(MaintenanceActionId.RepairSystemFiles)]
    public void RepairCannotProceedWithoutConfirmedRecovery(MaintenanceActionId repair)
    {
        var plan = new[] { MaintenanceActionId.ScanWindowsImage, repair };

        Assert.True(MaintenancePolicy.RequiresRestorePoint(plan));
        Assert.Throws<InvalidOperationException>(() => MaintenancePolicy.EnsureRestorePoint(plan, false));
        MaintenancePolicy.EnsureRestorePoint(plan, true);
    }

    [Theory]
    [InlineData(MaintenanceActionId.ScanWindowsImage)]
    [InlineData(MaintenanceActionId.VerifySystemFiles)]
    [InlineData(MaintenanceActionId.AnalyzeSystemDrive)]
    [InlineData(MaintenanceActionId.DefenderQuickScan)]
    public void DiagnosticAndDefenderOperationsRemainAvailableWithoutRecovery(MaintenanceActionId action)
    {
        var plan = new[] { action };

        Assert.False(MaintenancePolicy.RequiresRestorePoint(plan));
        MaintenancePolicy.EnsureRestorePoint(plan, false);
    }

    [Fact]
    public void EveryDefinedActionHasExactlyOneCatalogEntry()
    {
        var ids = Enum.GetValues<MaintenanceActionId>();

        Assert.Equal(ids.Length, MaintenanceCatalog.All.Count);
        Assert.Equal(ids.Length, MaintenanceCatalog.All.Select(action => action.Id).Distinct().Count());
        Assert.All(ids, id => Assert.Equal(id, MaintenanceCatalog.Get(id).Id));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    [InlineData(int.MaxValue)]
    public void UnrecognizedIdsAreRejectedBeforeExecution(int unrecognized)
    {
        var id = (MaintenanceActionId)unrecognized;

        Assert.Throws<ArgumentOutOfRangeException>(() => MaintenanceCatalog.Get(id));
        Assert.Throws<ArgumentOutOfRangeException>(() => MaintenancePolicy.ValidateAndOrder([MaintenanceActionId.ScanWindowsImage, id]));
        Assert.Throws<ArgumentOutOfRangeException>(() => MaintenancePolicy.EnsureRestorePoint([id], true));
    }

    [Fact]
    public void EmptyNullAndDuplicatePlansAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => MaintenancePolicy.ValidateAndOrder(null!));
        Assert.Throws<ArgumentException>(() => MaintenancePolicy.ValidateAndOrder([]));
        Assert.Throws<ArgumentException>(() => MaintenancePolicy.ValidateAndOrder([
            MaintenanceActionId.ScanWindowsImage,
            MaintenanceActionId.RepairWindowsImage,
            MaintenanceActionId.ScanWindowsImage]));
    }

    [Fact]
    public void ImageRepairPrecedesSystemFileOperationsRegardlessOfSelectionOrder()
    {
        MaintenanceActionId[] expected = [
            MaintenanceActionId.ScanWindowsImage,
            MaintenanceActionId.RepairWindowsImage,
            MaintenanceActionId.VerifySystemFiles,
            MaintenanceActionId.RepairSystemFiles,
            MaintenanceActionId.AnalyzeSystemDrive,
            MaintenanceActionId.DefenderQuickScan];

        Assert.Equal(expected, MaintenancePolicy.ValidateAndOrder(expected.Reverse()));
        Assert.Equal(expected, MaintenancePolicy.ValidateAndOrder(expected));
        Assert.Equal(Enum.GetValues<MaintenanceActionId>().Length,
            MaintenancePolicy.ValidateAndOrder(Enum.GetValues<MaintenanceActionId>()).Count);
    }

    [Fact]
    public void OrderingDoesNotSilentlyAddUnselectedActions()
    {
        var result = MaintenancePolicy.ValidateAndOrder([
            MaintenanceActionId.RepairSystemFiles,
            MaintenanceActionId.ScanWindowsImage]);

        Assert.Equal(new[] { MaintenanceActionId.ScanWindowsImage, MaintenanceActionId.RepairSystemFiles }, result);
    }

    [Fact]
    public void CatalogAndValidatedPlanCannotBeMutatedByConsumers()
    {
        var catalog = Assert.IsAssignableFrom<IList<MaintenanceActionDefinition>>(MaintenanceCatalog.All);
        var plan = Assert.IsAssignableFrom<IList<MaintenanceActionId>>(
            MaintenancePolicy.ValidateAndOrder([MaintenanceActionId.ScanWindowsImage]));

        Assert.Throws<NotSupportedException>(() => catalog.Clear());
        Assert.Throws<NotSupportedException>(() => plan.Add(MaintenanceActionId.RepairWindowsImage));
    }
}
