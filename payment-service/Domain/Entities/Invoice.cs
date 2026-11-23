using System;
using System.Collections.Generic;

namespace Domain.Entities
{
    public class Invoice
    {
        public Guid Id { get; set; }
        public Guid? SessionId { get; set; } // ID của ParkingSessionBilling; null for booking deposit
        public ParkingSessionBilling? Session { get; set; }
        public long? BookingId { get; set; } // Core booking id; cross-database reference, no FK
        public long? CoreSessionId { get; set; } // Core parking session id for counter collections
        public string Purpose { get; set; } = "PARKING_SESSION";

        public decimal TotalAmount { get; set; }
        public decimal? GrossAmount { get; set; } // Full checkout fee before applying any booking deposit
        public string Status { get; set; } // "Pending", "Paid", "Cancelled"
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? PaidAt { get; set; }

        public required ICollection<PaymentTransaction> Transactions { get; set; }
    }
}
