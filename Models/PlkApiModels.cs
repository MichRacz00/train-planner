using System.Text.Json.Serialization;

namespace TrainPlanner.Models;

// ── GET /api/v1/dictionaries/stations ──────────────────────────────────────────

public record PlkStationsResponse(
    [property: JsonPropertyName("stations")] List<PlkStationDto>? Stations,
    [property: JsonPropertyName("totalCount")] int TotalCount,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("pageSize")] int PageSize,
    [property: JsonPropertyName("totalPages")] int TotalPages);

public record PlkStationDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string? Name);

// ── GET /api/v1/schedules ─────────────────────────────────────────────────────

public record PlkScheduleResponse(
    [property: JsonPropertyName("generatedAt")] DateTime GeneratedAt,
    [property: JsonPropertyName("routes")] List<PlkRouteDto>? Routes,
    [property: JsonPropertyName("dictionaries")] PlkDictionariesDto? Dictionaries);

public record PlkRouteDto(
    [property: JsonPropertyName("scheduleId")] int ScheduleId,
    [property: JsonPropertyName("orderId")] int OrderId,
    [property: JsonPropertyName("trainOrderId")] int TrainOrderId,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("carrierCode")] string? CarrierCode,
    [property: JsonPropertyName("nationalNumber")] string? NationalNumber,
    [property: JsonPropertyName("commercialCategorySymbol")] string? CommercialCategorySymbol,
    [property: JsonPropertyName("operatingDates")] List<DateOnly>? OperatingDates,
    [property: JsonPropertyName("stations")] List<PlkStationOnRouteDto>? Stations);

public record PlkStationOnRouteDto(
    [property: JsonPropertyName("stationId")] int StationId,
    [property: JsonPropertyName("orderNumber")] int OrderNumber,
    [property: JsonPropertyName("arrivalTime")] string? ArrivalTime,
    [property: JsonPropertyName("arrivalDay")] int? ArrivalDay,
    [property: JsonPropertyName("departureTime")] string? DepartureTime,
    [property: JsonPropertyName("departureDay")] int? DepartureDay,
    [property: JsonPropertyName("arrivalPlatform")] string? ArrivalPlatform,
    [property: JsonPropertyName("departurePlatform")] string? DeparturePlatform,
    [property: JsonPropertyName("arrivalTrack")] string? ArrivalTrack,
    [property: JsonPropertyName("departureTrack")] string? DepartureTrack,
    [property: JsonPropertyName("arrivalTrainNumber")] string? ArrivalTrainNumber,
    [property: JsonPropertyName("departureTrainNumber")] string? DepartureTrainNumber,
    [property: JsonPropertyName("arrivalCommercialCategory")] string? ArrivalCommercialCategory,
    [property: JsonPropertyName("departureCommercialCategory")] string? DepartureCommercialCategory,
    [property: JsonPropertyName("stopTypeId")] int? StopTypeId,
    [property: JsonPropertyName("stopTypeName")] string? StopTypeName);

public record PlkDictionariesDto(
    [property: JsonPropertyName("stations")] Dictionary<string, PlkStationDto>? Stations,
    [property: JsonPropertyName("connectionTypes")] Dictionary<string, string>? ConnectionTypes,
    [property: JsonPropertyName("carriers")] Dictionary<string, string>? Carriers,
    [property: JsonPropertyName("commercialCategories")] Dictionary<string, string>? CommercialCategories,
    [property: JsonPropertyName("stopTypes")] Dictionary<string, string>? StopTypes);

// ── GET /api/v1/operations ────────────────────────────────────────────────────

public record PlkOperationResponse(
    [property: JsonPropertyName("generatedAt")] DateTime GeneratedAt,
    [property: JsonPropertyName("trains")] List<PlkTrainOperationDto>? Trains,
    [property: JsonPropertyName("stations")] Dictionary<string, string>? Stations);

public record PlkTrainOperationDto(
    [property: JsonPropertyName("scheduleId")] int ScheduleId,
    [property: JsonPropertyName("orderId")] int OrderId,
    [property: JsonPropertyName("trainOrderId")] int TrainOrderId,
    [property: JsonPropertyName("operatingDate")] DateOnly OperatingDate,
    [property: JsonPropertyName("trainStatus")] string? TrainStatus,
    [property: JsonPropertyName("stations")] List<PlkOperationStationDto>? Stations);

public record PlkOperationStationDto(
    [property: JsonPropertyName("stationId")] int StationId,
    [property: JsonPropertyName("plannedSequenceNumber")] int? PlannedSequenceNumber,
    [property: JsonPropertyName("actualSequenceNumber")] int ActualSequenceNumber,
    [property: JsonPropertyName("plannedDeparture")] DateTime? PlannedDeparture,
    [property: JsonPropertyName("plannedArrival")] DateTime? PlannedArrival,
    [property: JsonPropertyName("actualDeparture")] DateTime? ActualDeparture,
    [property: JsonPropertyName("actualArrival")] DateTime? ActualArrival,
    [property: JsonPropertyName("departureDelayMinutes")] int? DepartureDelayMinutes,
    [property: JsonPropertyName("arrivalDelayMinutes")] int? ArrivalDelayMinutes,
    [property: JsonPropertyName("isConfirmed")] bool IsConfirmed,
    [property: JsonPropertyName("isCancelled")] bool IsCancelled);
