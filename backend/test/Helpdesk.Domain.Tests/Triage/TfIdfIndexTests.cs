using System;
using System.Linq;
using Shouldly;
using Xunit;

namespace Helpdesk.Triage;

public class TfIdfIndexTests
{
    private static readonly Guid Vpn = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Printer = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Aircon = Guid.Parse("00000000-0000-0000-0000-000000000003");

    private static TfIdfIndex Index() => new(new[]
    {
        (Vpn, "VPN disconnects every few minutes when working from home"),
        (Printer, "Printer on floor two jams and shows a paper error"),
        (Aircon, "Air conditioner in room 204 leaks water on the floor"),
    });

    [Fact]
    public void The_most_similar_document_comes_first()
    {
        Index().Search("my vpn keeps disconnecting", 3).First().Id.ShouldBe(Vpn);
        Index().Search("paper jam in the printer", 3).First().Id.ShouldBe(Printer);
        Index().Search("water leaking from the air conditioner", 3).First().Id.ShouldBe(Aircon);
    }

    [Fact]
    public void Word_endings_do_not_stop_a_match()
    {
        // jams / jam, disconnects / disconnect
        TfIdfIndex.Tokenize("printer jams").ShouldContain("jam");
        TfIdfIndex.Tokenize("disconnects").ShouldBe(TfIdfIndex.Tokenize("disconnect"));
    }

    [Fact]
    public void Case_and_punctuation_do_not_matter()
    {
        var shout = Index().Search("VPN!!! DISCONNECTS...", 1).Single();
        var calm = Index().Search("vpn disconnects", 1).Single();

        shout.Id.ShouldBe(calm.Id);
        shout.Score.ShouldBe(calm.Score, tolerance: 1e-9);
    }

    [Fact]
    public void Filler_words_alone_match_nothing()
    {
        Index().Search("please can you help me with the", 3).ShouldBeEmpty();
    }

    [Fact]
    public void Text_with_no_shared_words_matches_nothing()
    {
        Index().Search("quarterly budget forecast", 3).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_empty_query_matches_nothing(string? query)
    {
        Index().Search(query!, 3).ShouldBeEmpty();
    }

    [Fact]
    public void An_identical_text_scores_one_and_every_score_stays_between_zero_and_one()
    {
        var results = Index().Search("Air conditioner in room 204 leaks water on the floor", 3);

        results.First().Score.ShouldBe(1.0, tolerance: 1e-9);
        results.ShouldAllBe(match => match.Score > 0 && match.Score <= 1.0000001);
    }

    [Fact]
    public void A_document_can_be_left_out_so_a_ticket_never_matches_itself()
    {
        Index().Search("vpn disconnects", 3, exclude: Vpn).ShouldNotContain(match => match.Id == Vpn);
    }

    [Fact]
    public void A_minimum_score_filters_weak_matches()
    {
        var weak = Index().Search("floor", 3);
        weak.ShouldNotBeEmpty();

        Index().Search("floor", 3, minScore: 0.9).ShouldBeEmpty();
    }

    [Fact]
    public void Only_the_requested_number_of_results_comes_back_best_first()
    {
        var results = Index().Search("floor printer water", 2);

        results.Count.ShouldBe(2);
        results[0].Score.ShouldBeGreaterThanOrEqualTo(results[1].Score);
    }

    [Fact]
    public void A_rare_word_counts_more_than_a_common_one()
    {
        var index = new TfIdfIndex(new[]
        {
            (Vpn, "login problem with the portal"),
            (Printer, "login problem with the printer"),
            (Aircon, "login problem with the lift"),
        });

        // "printer" appears in one document, "login problem" in all three: the rare word decides
        index.Search("login problem printer", 3).First().Id.ShouldBe(Printer);
    }

    [Fact]
    public void Words_with_accents_are_kept_whole()
    {
        var index = new TfIdfIndex(new[]
        {
            (Vpn, "Máy in bị kẹt giấy liên tục"),
            (Printer, "Điều hòa phòng họp bị chảy nước"),
        });

        index.Search("máy in kẹt giấy", 2).First().Id.ShouldBe(Vpn);
        index.Search("điều hòa chảy nước", 2).First().Id.ShouldBe(Printer);
    }

    [Fact]
    public void An_empty_index_answers_with_nothing()
    {
        var index = new TfIdfIndex(Array.Empty<(Guid, string)>());

        index.Count.ShouldBe(0);
        index.Search("anything", 3).ShouldBeEmpty();
    }

    [Fact]
    public void The_same_input_always_gives_the_same_order()
    {
        var first = Index().Search("floor", 3).Select(match => match.Id).ToList();
        var second = Index().Search("floor", 3).Select(match => match.Id).ToList();

        first.ShouldBe(second);
    }
}
