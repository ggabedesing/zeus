using Zeus.Core;

namespace Zeus.Core.Tests;

public sealed class MaintenancePolicyTests
{
    [Theory]
    [InlineData(MaintenanceActionId.RepairWindowsImage)]
    [InlineData(MaintenanceActionId.RepairSystemFiles)]
    public void RepairCannotProceedWithoutConfirmedRecovery(MaintenanceActionId repair)
    {
        var plan = new[] { repair };

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
    [InlineData(12)]
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

    [Theory]
    [InlineData(MaintenanceActionId.ScanWindowsImage, MaintenanceActionId.RepairWindowsImage)]
    [InlineData(MaintenanceActionId.ScanWindowsImage, MaintenanceActionId.RepairSystemFiles)]
    [InlineData(MaintenanceActionId.VerifySystemFiles, MaintenanceActionId.RepairWindowsImage)]
    [InlineData(MaintenanceActionId.VerifySystemFiles, MaintenanceActionId.RepairSystemFiles)]
    public void DiagnosticScansAndRepairsRequireSeparateReviewedPlans(MaintenanceActionId scan, MaintenanceActionId repair)
    {
        var exception = Assert.Throws<ArgumentException>(() => MaintenancePolicy.ValidateAndOrder([scan, repair]));
        Assert.Contains("revise o resultado", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompatibleScansAndRepairsKeepTheirStableIndependentOrder()
    {
        MaintenanceActionId[] scanPlan = [
            MaintenanceActionId.ScanWindowsImage,
            MaintenanceActionId.VerifySystemFiles,
            MaintenanceActionId.AnalyzeSystemDrive,
            MaintenanceActionId.DefenderQuickScan];
        MaintenanceActionId[] repairPlan = [MaintenanceActionId.RepairWindowsImage, MaintenanceActionId.RepairSystemFiles];

        Assert.Equal(scanPlan, MaintenancePolicy.ValidateAndOrder(scanPlan.Reverse()));
        Assert.Equal(repairPlan, MaintenancePolicy.ValidateAndOrder(repairPlan.Reverse()));
        Assert.Equal(Enum.GetValues<MaintenanceActionId>().Length, MaintenanceCatalog.All.Count);
    }

    [Fact]
    public void OrderingDoesNotSilentlyAddUnselectedActions()
    {
        var result = MaintenancePolicy.ValidateAndOrder([
            MaintenanceActionId.DefenderQuickScan,
            MaintenanceActionId.VerifySystemFiles]);

        Assert.Equal(new[] { MaintenanceActionId.VerifySystemFiles, MaintenanceActionId.DefenderQuickScan }, result);
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
