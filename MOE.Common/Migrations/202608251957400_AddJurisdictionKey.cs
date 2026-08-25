namespace MOE.Common.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddJurisdictionKey : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Jurisdictions", "JurisdictionKey", c => c.String(maxLength: 255));
        }
        
        public override void Down()
        {
            DropColumn("dbo.Jurisdictions", "JurisdictionKey");
        }
    }
}
