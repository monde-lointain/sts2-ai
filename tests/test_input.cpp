#include <sstream>
#include <string>

#include "input/Input.h"
#include "test_runner.h"

TEST(input_read_action_e_returns_end_turn) {
    std::istringstream in("e\n");
    auto a = input::read_action(in);
    CHECK(a.kind == input::Action::EndTurn);
}

TEST(input_read_action_E_uppercase_returns_end_turn) {
    std::istringstream in("E\n");
    auto a = input::read_action(in);
    CHECK(a.kind == input::Action::EndTurn);
}

TEST(input_read_action_q_returns_quit) {
    std::istringstream in("q\n");
    auto a = input::read_action(in);
    CHECK(a.kind == input::Action::Quit);
}

TEST(input_read_action_digit_returns_play_card_with_index) {
    std::istringstream in("3\n");
    auto a = input::read_action(in);
    CHECK(a.kind == input::Action::PlayCard);
    CHECK(a.card_idx == 3);
}

TEST(input_read_action_multi_digit_index) {
    std::istringstream in("12\n");
    auto a = input::read_action(in);
    CHECK(a.kind == input::Action::PlayCard);
    CHECK(a.card_idx == 12);
}

TEST(input_read_action_empty_returns_invalid) {
    std::istringstream in("\n");
    auto a = input::read_action(in);
    CHECK(a.kind == input::Action::Invalid);
}

TEST(input_read_action_garbage_returns_invalid) {
    std::istringstream in("foo\n");
    auto a = input::read_action(in);
    CHECK(a.kind == input::Action::Invalid);
}

TEST(input_read_action_trims_whitespace) {
    std::istringstream in("   e   \n");
    auto a = input::read_action(in);
    CHECK(a.kind == input::Action::EndTurn);
}

TEST(input_read_action_eof_returns_quit) {
    std::istringstream in("");
    auto a = input::read_action(in);
    CHECK(a.kind == input::Action::Quit);
}

TEST(input_read_index_valid_in_range) {
    std::istringstream in("2\n");
    CHECK(input::read_index(in, 5) == 2);
}

TEST(input_read_index_out_of_range_returns_negative) {
    std::istringstream in("99\n");
    CHECK(input::read_index(in, 5) == -1);
}

TEST(input_read_index_non_numeric_returns_negative) {
    std::istringstream in("abc\n");
    CHECK(input::read_index(in, 5) == -1);
}
