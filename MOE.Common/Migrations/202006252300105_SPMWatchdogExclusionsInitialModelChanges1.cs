namespace MOE.Common.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class SPMWatchdogExclusionsInitialModelChanges1 : DbMigration
    {
        public override void Up()
        {
            AlterColumn("dbo.SPMWatchdogExclusions", "PhaseID", c => c.Int());
        }
        
        public override void Down()
        {
            AlterColumn("dbo.SPMWatchdogExclusions", "PhaseID", c => c.Int(nullable: false));
        }
    }
}
