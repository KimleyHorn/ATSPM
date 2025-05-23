using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AtspmApi.Models
{
    public class RouteSignal
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [Display(Name = "Route")]
        public int RouteId { get; set; }

        public virtual Route Route { get; set; }

        [Required]
        [Display(Name = "Signal Order")]
        public int Order { get; set; }

        [Required]

        [Display(Name = "Signal")]
        [StringLength(10)]
        public string SignalId { get; set; }

        [NotMapped]
        public Signal Signal { get; set; }

       // public List<RoutePhaseDirection> PhaseDirections { get; set; }
    }
}