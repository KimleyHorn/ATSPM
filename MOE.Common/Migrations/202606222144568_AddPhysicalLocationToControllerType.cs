namespace MOE.Common.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddPhysicalLocationToControllerType : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.ControllerTypes", "PhysicalLocation", c => c.String(maxLength: 50));
        }
        
        public override void Down()
        {
            DropColumn("dbo.ControllerTypes", "PhysicalLocation");
        }
    }
}
